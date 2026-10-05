import { Server, type Connection, type ConnectionContext } from 'partyserver';
import { and, eq, gt, lte } from 'drizzle-orm';
import { createDb } from '../db/client';
import { gameRooms, roomMembers } from '../db/schema';
import type { Env } from '../env';
import { ALARM_MS, authenticate, deleteEmptyRoom, LEASE_MS, numericId, removeMember } from '../rooms';
import { parseJson } from '../utils/json';

type MemberState = { playerId: string; ready: boolean };
type Signal = { type: string; to?: string; sdp?: string; candidate?: string; sdpMid?: string; sdpMLineIndex?: number };

/** One coordinator per room. Only SDP/ICE is relayed; gameplay uses a full WebRTC mesh. */
export class Room extends Server<Env> {
  static options = { hibernate: true };

  private members() {
    return [...this.getConnections<MemberState>()].filter((c) => c.state?.ready);
  }

  private announceLeft(playerId: string, without?: string) {
    for (const peer of this.members()) {
      if (peer.id !== without) {
        peer.send(JSON.stringify({ type: 'peer-left', from: playerId }));
      }
    }
  }

  private async schedule() {
    // Never postpone an earlier cleanup alarm when more users join.
    const alarm = await this.ctx.storage.getAlarm();
    if (alarm === null) {
      await this.ctx.storage.setAlarm(Date.now() + ALARM_MS);
    }
  }

  async scheduleCleanup(): Promise<void> {
    const roomId = numericId(this.name);
    if (roomId === null) throw new Error('invalid room id');
    // Persist the room ID so cleanup can resume after hibernation.
    await this.ctx.storage.put('roomId', roomId);
    await this.schedule();
  }

  async leave(playerId: number): Promise<void> {
    const roomId = numericId(this.name);
    if (roomId === null) throw new Error('invalid room id');
    const removed = await removeMember(this.env.DB, roomId, playerId);
    if (removed) this.announceLeft(String(playerId));
    for (const connection of this.getConnections<MemberState>()) {
      if (connection.state?.playerId === String(playerId)) {
        connection.setState({ playerId: String(playerId), ready: false });
        connection.close(1000, 'left room');
      }
    }
  }

  async onConnect(connection: Connection<MemberState>, context: ConnectionContext) {
    const url = new URL(context.request.url);
    const roomId = numericId(this.name);
    const playerId = await authenticate(this.env.DB, url.searchParams.get('playerId'), url.searchParams.get('token'));
    if (roomId === null || playerId === null) {
      connection.close(4001, 'invalid credentials');
      return;
    }
    const now = Date.now();
    const adopted = await createDb(this.env.DB)
      .update(roomMembers)
      .set({ connectionId: connection.id, expiresAt: now + LEASE_MS })
      .where(and(eq(roomMembers.roomId, roomId), eq(roomMembers.playerId, playerId), gt(roomMembers.expiresAt, now)))
      .run();
    if (adopted.meta.changes !== 1) {
      connection.close(4003, 'join the room using HTTP first');
      return;
    }
    connection.setState({ playerId: String(playerId), ready: false });
    for (const previous of this.getConnections<MemberState>()) {
      if (previous.id !== connection.id && previous.state?.playerId === String(playerId)) {
        if (previous.state.ready) this.announceLeft(String(playerId), previous.id);
        previous.setState({ playerId: String(playerId), ready: false });
        previous.close(4002, 'connection replaced');
      }
    }
    await this.schedule();
  }

  async onMessage(connection: Connection<MemberState>, raw: string | ArrayBuffer | ArrayBufferView) {
    // The protocol uses JSON text frames and specifies no application size limit.
    // Cloudflare bounds WebSocket receives: https://developers.cloudflare.com/durable-objects/platform/limits/
    if (!connection.state || typeof raw !== 'string') return;
    const message = parseJson<Signal>(raw);
    if (!message || typeof message.type !== 'string') return;
    const playerId = connection.state.playerId;
    if (message.type === 'heartbeat' || message.type === 'client-ready') {
      const now = Date.now();
      const renewed = await createDb(this.env.DB)
        .update(roomMembers)
        .set({ expiresAt: now + LEASE_MS })
        .where(
          and(
            eq(roomMembers.roomId, Number(this.name)),
            eq(roomMembers.playerId, Number(playerId)),
            eq(roomMembers.connectionId, connection.id),
            gt(roomMembers.expiresAt, now),
          ),
        )
        .run();
      if (renewed.meta.changes !== 1) {
        connection.close(4003, 'membership expired');
        return;
      }
      if (message.type === 'heartbeat') {
        connection.send(JSON.stringify({ type: 'heartbeat' }));
      } else if (!connection.state.ready) {
        // Existing peers prepare answerer connections before the newcomer can send offers.
        const peers = this.members().filter((peer) => peer.id !== connection.id);
        connection.setState({ playerId, ready: true });
        for (const peer of peers) peer.send(JSON.stringify({ type: 'peer-joined', from: playerId, isInitiator: false }));
        connection.send(JSON.stringify({ type: 'room-joined', peers: peers.map((p) => p.state!.playerId), isInitiator: true }));
      }
      return;
    }
    if (!connection.state.ready || !['offer', 'answer', 'ice-candidate'].includes(message.type)) return;
    if (typeof message.to !== 'string' || message.to === playerId) return;
    if ((message.type === 'offer' || message.type === 'answer') && typeof message.sdp !== 'string') return;
    if (message.type === 'ice-candidate' && typeof message.candidate !== 'string') return;
    const target = this.members().find((peer) => peer.state!.playerId === message.to);
    // The source is always the authenticated socket, never a client-supplied ID.
    target?.send(
      JSON.stringify({
        type: message.type,
        from: playerId,
        to: message.to,
        sdp: message.sdp,
        candidate: message.candidate,
        sdpMid: message.sdpMid,
        sdpMLineIndex: message.sdpMLineIndex,
      }),
    );
  }

  async onClose(connection: Connection<MemberState>) {
    if (!connection.state) return;
    const removed = await removeMember(this.env.DB, Number(this.name), Number(connection.state.playerId), connection.id);
    if (removed && connection.state.ready) this.announceLeft(connection.state.playerId, connection.id);
  }

  async onError(connection: Connection<MemberState>) {
    await this.onClose(connection);
    connection.close(1011, 'socket error');
  }

  async onAlarm() {
    const roomId = await this.ctx.storage.get<number>('roomId');
    if (roomId === undefined) return; // Ignore legacy 1:1 alarms after upgrading.
    const db = createDb(this.env.DB);
    const result = await db.batch([
      db
        .delete(roomMembers)
        .where(and(eq(roomMembers.roomId, roomId), lte(roomMembers.expiresAt, Date.now())))
        .returning({ playerId: roomMembers.playerId, connectionId: roomMembers.connectionId }),
      deleteEmptyRoom(db, roomId),
    ]);
    for (const member of result[0]) {
      if (!member.connectionId) continue;
      const connection = this.getConnection<MemberState>(member.connectionId);
      if (connection?.state?.ready) {
        this.announceLeft(String(member.playerId), connection.id);
      }
      if (connection) {
        connection.setState({ playerId: String(member.playerId), ready: false });
        connection.close(4003, 'heartbeat timeout');
      }
    }
    const room = await db.select({ id: gameRooms.id }).from(gameRooms).where(eq(gameRooms.id, roomId)).get();
    if (room) {
      await this.ctx.storage.setAlarm(Date.now() + ALARM_MS);
    }
  }
}
