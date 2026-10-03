import { Server, type Connection } from 'partyserver';
import type { Env } from '../env';
import { listRooms } from '../rooms';

type Subscription = { lastRoomId: number };

/** Room discovery stream: /parties/lobby/rooms. */
export class Lobby extends Server<Env> {
  static options = { hibernate: true };
  // Serialize D1 catch-up and publication. Cursors survive hibernation in attachments.
  private pending: Promise<void> = Promise.resolve();
  private enqueue(action: () => Promise<void>) {
    const result = this.pending.then(action);
    this.pending = result.catch(() => {});
    return result;
  }

  onConnect(connection: Connection) {
    if (this.name !== 'rooms') connection.close(4004, 'use /parties/lobby/rooms');
  }

  async onMessage(connection: Connection<Subscription>, raw: string | ArrayBuffer | ArrayBufferView) {
    if (typeof raw !== 'string' || raw.length > 1024) {
      connection.close(4000, 'invalid message');
      return;
    }
    let message: { type?: string; lastRoomId?: unknown };
    try {
      message = JSON.parse(raw);
    } catch {
      connection.close(4000, 'invalid JSON');
      return;
    }
    if (!message || typeof message !== 'object') {
      connection.close(4000, 'invalid message');
      return;
    }
    if (message.type === 'ping') {
      connection.send(JSON.stringify({ type: 'pong' }));
      return;
    }
    const cursor = message.lastRoomId;
    if (message.type !== 'subscribe' || typeof cursor !== 'number' || !Number.isSafeInteger(cursor) || cursor < 0) {
      connection.close(4000, 'send subscribe with a non-negative integer lastRoomId');
      return;
    }
    await this.enqueue(async () => {
      if (connection.readyState !== WebSocket.OPEN) return;
      connection.setState({ lastRoomId: cursor });
      await this.sendNewRooms([connection]);
      if (connection.readyState === WebSocket.OPEN) connection.send(JSON.stringify({ type: 'subscribed' }));
    });
  }

  private async sendNewRooms(connections: Connection<Subscription>[]) {
    const active = connections.filter((c) => c.state && c.readyState === WebSocket.OPEN);
    if (active.length === 0) return;
    const cursor = active.reduce((min, c) => Math.min(min, c.state!.lastRoomId), Number.MAX_SAFE_INTEGER);
    const rooms = await listRooms(this.env.DB, cursor);
    for (const connection of active) {
      if (connection.readyState !== WebSocket.OPEN) continue;
      const delta = rooms.filter((room) => room.id > connection.state!.lastRoomId);
      if (delta.length === 0) continue;
      connection.send(JSON.stringify({ type: 'rooms-created', rooms: delta }));
      connection.setState({ lastRoomId: delta[delta.length - 1].id });
    }
  }

  async onRequest(request: Request): Promise<Response> {
    if (this.name !== 'rooms' || request.method !== 'POST' || new URL(request.url).pathname !== '/publish')
      return new Response('not found', { status: 404 });
    // Read committed rows: concurrent creation notifications may arrive out of order.
    await this.enqueue(() => this.sendNewRooms([...this.getConnections<Subscription>()]));
    return new Response('ok');
  }
}
