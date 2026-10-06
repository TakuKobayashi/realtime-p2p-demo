import { Server, type Connection } from 'partyserver';
import { and, eq, gt, lte, notExists } from 'drizzle-orm';
import { createDb } from '../db/client';
import { gameRooms, roomMembers } from '../db/schema';
import type { Env } from '../env';
import { ALARM_MS, authenticate, LEASE_MS, numericId, removeMember } from '../rooms';
import { parseJson } from '../utils/json';

type SocketState = { connectedAt: number; roomId?: number; playerId?: string };
type Signal = {
  type: string;
  roomId?: unknown;
  playerId?: unknown;
  token?: unknown;
  to?: string;
  sdp?: string;
  candidate?: string;
  sdpMid?: string;
  sdpMLineIndex?: number;
};

/** Demo signaling endpoint. Room membership is established by a message, never by the connection URL. */
export class Signaling extends Server<Env> {
  static options = { hibernate: true };
  private membershipOperation: Promise<void> = Promise.resolve();

  // D1 calls yield. Serialize membership changes so peer discovery and socket replacement
  // cannot announce conflicting offerer roles while authentication/adoption is in flight.
  private membership<T>(operation: () => Promise<T>): Promise<T> {
    const pending = this.membershipOperation.then(operation);
    this.membershipOperation = pending.then(
      () => {},
      () => {},
    );
    return pending;
  }
  private members(roomId: number) {
    return [...this.getConnections<SocketState>()].filter((c) => c.state?.roomId === roomId && c.state.playerId !== undefined);
  }
  private announceLeft(roomId: number, playerId: string, without?: string) {
    for (const peer of this.members(roomId)) {
      if (peer.id !== without) peer.send(JSON.stringify({ type: 'peer-left', from: playerId }));
    }
  }
  async scheduleCleanup(): Promise<void> {
    if ((await this.ctx.storage.getAlarm()) === null) await this.ctx.storage.setAlarm(Date.now() + ALARM_MS);
  }
  async leave(roomId: number, playerId: number): Promise<void> {
    await this.membership(async () => {
      const removed = await removeMember(this.env.DB, roomId, playerId);
      if (removed) this.announceLeft(roomId, String(playerId));
      for (const connection of this.members(roomId)) {
        if (connection.state?.playerId === String(playerId)) {
          connection.setState({ connectedAt: connection.state.connectedAt });
          connection.close(1000, 'left room');
        }
      }
    });
  }
  async onConnect(connection: Connection<SocketState>) {
    connection.setState({ connectedAt: Date.now() });
    // An unauthenticated socket has one reservation lease interval to send its join message.
    await this.scheduleCleanup();
  }
  private async join(connection: Connection<SocketState>, message: Signal) {
    await this.membership(async () => {
      if (connection.readyState !== WebSocket.OPEN) return;
      if (connection.state?.roomId !== undefined) {
        connection.close(1008, 'already joined');
        return;
      }
      const roomId = numericId(typeof message.roomId === 'number' ? String(message.roomId) : message.roomId);
      const playerId = await authenticate(this.env.DB, message.playerId, message.token);
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
      if (connection.readyState !== WebSocket.OPEN) {
        await removeMember(this.env.DB, roomId, playerId, connection.id);
        return;
      }
      for (const previous of this.members(roomId)) {
        if (previous.id !== connection.id && previous.state?.playerId === String(playerId)) {
          this.announceLeft(roomId, String(playerId), previous.id);
          previous.setState({ connectedAt: previous.state.connectedAt });
          previous.close(4002, 'connection replaced');
        }
      }
      const peers = this.members(roomId);
      connection.setState({ connectedAt: connection.state?.connectedAt ?? now, roomId, playerId: String(playerId) });
      // Notify answerers before the newcomer can send an offer after its acknowledgement.
      for (const peer of peers) peer.send(JSON.stringify({ type: 'peer-joined', from: String(playerId), isInitiator: false }));
      connection.send(JSON.stringify({ type: 'room-joined', peers: peers.map((peer) => peer.state!.playerId!), isInitiator: true }));
      await this.scheduleCleanup();
    });
  }
  async onMessage(connection: Connection<SocketState>, raw: string | ArrayBuffer | ArrayBufferView) {
    if (typeof raw !== 'string') return;
    const message = parseJson<Signal>(raw);
    if (!message || typeof message.type !== 'string') return;
    if (message.type === 'join') {
      await this.join(connection, message);
      return;
    }
    const state = connection.state;
    if (state?.roomId === undefined || state.playerId === undefined) {
      connection.close(4001, 'join required');
      return;
    }
    if (message.type === 'heartbeat') {
      const now = Date.now();
      const renewed = await createDb(this.env.DB)
        .update(roomMembers)
        .set({ expiresAt: now + LEASE_MS })
        .where(
          and(
            eq(roomMembers.roomId, state.roomId),
            eq(roomMembers.playerId, Number(state.playerId)),
            eq(roomMembers.connectionId, connection.id),
            gt(roomMembers.expiresAt, now),
          ),
        )
        .run();
      if (renewed.meta.changes !== 1) {
        connection.close(4003, 'membership expired');
        return;
      }
      connection.send(JSON.stringify({ type: 'heartbeat' }));
      return;
    }
    if (!['offer', 'answer', 'ice-candidate'].includes(message.type) || typeof message.to !== 'string' || message.to === state.playerId)
      return;
    if ((message.type === 'offer' || message.type === 'answer') && typeof message.sdp !== 'string') return;
    if (message.type === 'ice-candidate' && typeof message.candidate !== 'string') return;
    // Scope delivery to the authenticated membership; ignore client-supplied room/source fields.
    const target = this.members(state.roomId).find((peer) => peer.state?.playerId === message.to);
    target?.send(
      JSON.stringify({
        type: message.type,
        from: state.playerId,
        to: message.to,
        sdp: message.sdp,
        candidate: message.candidate,
        sdpMid: message.sdpMid,
        sdpMLineIndex: message.sdpMLineIndex,
      }),
    );
  }
  async onClose(connection: Connection<SocketState>) {
    await this.membership(async () => {
      const state = connection.state;
      if (state?.roomId === undefined || state.playerId === undefined) return;
      const removed = await removeMember(this.env.DB, state.roomId, Number(state.playerId), connection.id);
      if (removed) this.announceLeft(state.roomId, state.playerId, connection.id);
    });
  }
  async onError(connection: Connection<SocketState>) {
    await this.onClose(connection);
    connection.close(1011, 'socket error');
  }
  async onAlarm() {
    await this.membership(async () => {
      const db = createDb(this.env.DB);
      const expired = await db
        .delete(roomMembers)
        .where(lte(roomMembers.expiresAt, Date.now()))
        .returning({ roomId: roomMembers.roomId, playerId: roomMembers.playerId, connectionId: roomMembers.connectionId });
      // HTTP admission also prunes expired reservations. Sweep every empty room,
      // including those whose last reservation was already removed by an HTTP request.
      await db
        .delete(gameRooms)
        .where(notExists(db.select({ id: roomMembers.id }).from(roomMembers).where(eq(roomMembers.roomId, gameRooms.id))));
      for (const member of expired) {
        if (!member.connectionId) continue;
        const connection = this.getConnection<SocketState>(member.connectionId);
        if (connection?.state?.roomId === member.roomId && connection.state.playerId === String(member.playerId)) {
          this.announceLeft(member.roomId, String(member.playerId), connection.id);
          connection.setState({ connectedAt: connection.state.connectedAt });
          connection.close(4003, 'heartbeat timeout');
        }
      }
      for (const connection of this.getConnections<SocketState>()) {
        if (connection.state?.roomId === undefined && Date.now() - (connection.state?.connectedAt ?? 0) >= LEASE_MS)
          connection.close(4001, 'join timeout');
      }
      const remaining = await db.select({ id: gameRooms.id }).from(gameRooms).limit(1).get();
      if (remaining || [...this.getConnections()].some((connection) => connection.readyState === WebSocket.OPEN))
        await this.ctx.storage.setAlarm(Date.now() + ALARM_MS);
    });
  }
}
