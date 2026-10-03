import { Server, type Connection, type ConnectionContext } from 'partyserver';
import type { Env } from '../env';
import { ALARM_MS, authenticate, LEASE_MS, numericId, removeMember } from '../rooms';

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
      if (peer.id !== without) peer.send(JSON.stringify({ type: 'peer-left', from: playerId }));
    }
  }
  private async schedule() {
    // Never postpone an earlier cleanup alarm when more users join.
    const alarm = await this.ctx.storage.getAlarm();
    if (alarm === null) await this.ctx.storage.setAlarm(Date.now() + ALARM_MS);
  }

  async onRequest(request: Request): Promise<Response> {
    const roomId = numericId(this.name);
    if (roomId === null) return new Response('invalid room id', { status: 400 });
    const path = new URL(request.url).pathname;
    if (request.method === 'POST' && path === '/schedule') {
      // Older local workerd does not persist ctx.id.name in alarm records.
      await this.ctx.storage.put('roomId', roomId);
      await this.schedule();
      return new Response('ok');
    }
    if (request.method === 'POST' && path === '/leave') {
      const { playerId } = await request.json<{ playerId: number }>();
      const removed = await removeMember(this.env.DB, roomId, playerId);
      if (removed) this.announceLeft(String(playerId));
      for (const connection of this.getConnections<MemberState>()) {
        if (connection.state?.playerId === String(playerId)) {
          connection.setState({ playerId: String(playerId), ready: false });
          connection.close(1000, 'left room');
        }
      }
      return Response.json({ status: 'ok' });
    }
    return new Response('not found', { status: 404 });
  }

  async onConnect(connection: Connection<MemberState>, context: ConnectionContext) {
    const url = new URL(context.request.url);
    const roomId = numericId(this.name);
    const playerId = await authenticate(this.env.DB, url.searchParams.get('playerId'), url.searchParams.get('token'));
    if (roomId === null || playerId === null) {
      connection.close(4001, 'invalid credentials');
      return;
    }
    const adopted = await this.env.DB.prepare(
      `UPDATE room_members SET connection_id = ?, expires_at = ?
      WHERE room_id = ? AND player_id = ? AND expires_at > ?`,
    )
      .bind(connection.id, Date.now() + LEASE_MS, roomId, playerId, Date.now())
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
    if (!connection.state || typeof raw !== 'string' || raw.length > 131072) return;
    let message: Signal;
    try {
      message = JSON.parse(raw) as Signal;
    } catch {
      return;
    }
    if (!message || typeof message.type !== 'string') return;
    const playerId = connection.state.playerId;
    if (message.type === 'heartbeat' || message.type === 'client-ready') {
      const renewed = await this.env.DB.prepare(
        `UPDATE room_members SET expires_at = ?
        WHERE room_id = ? AND player_id = ? AND connection_id = ? AND expires_at > ?`,
      )
        .bind(Date.now() + LEASE_MS, Number(this.name), Number(playerId), connection.id, Date.now())
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
    const result = await this.env.DB.batch([
      this.env.DB.prepare('DELETE FROM room_members WHERE room_id = ? AND expires_at <= ? RETURNING player_id, connection_id').bind(
        roomId,
        Date.now(),
      ),
      this.env.DB.prepare('DELETE FROM game_rooms WHERE id = ? AND NOT EXISTS (SELECT 1 FROM room_members WHERE room_id = ?)').bind(
        roomId,
        roomId,
      ),
    ]);
    for (const member of result[0].results as { player_id: number; connection_id: string | null }[]) {
      if (!member.connection_id) continue;
      const connection = this.getConnection<MemberState>(member.connection_id);
      if (connection?.state?.ready) this.announceLeft(String(member.player_id), connection.id);
      if (connection) {
        connection.setState({ playerId: String(member.player_id), ready: false });
        connection.close(4003, 'heartbeat timeout');
      }
    }
    const room = await this.env.DB.prepare('SELECT id FROM game_rooms WHERE id = ?').bind(roomId).first();
    if (room) await this.ctx.storage.setAlarm(Date.now() + ALARM_MS);
  }
}
