import { Server, type Connection } from "partyserver";
import type { Env } from "../env";

/**
 * One Lobby Durable Object instance per player, keyed by playerId
 * (this.name === playerId). Reached by the Unity client at:
 *   wss://<worker-host>/parties/lobby/{playerId}
 *
 * The Unity client connects here right before calling POST /api/matchmaking/join
 * so that if it ends up waiting, it can still be notified the instant another
 * player's join request matches it - without polling.
 *
 * routes/matchmaking.ts pushes the notification by calling this same Durable
 * Object's fetch() directly (same worker, no network hop - see onRequest below).
 */
export class Lobby extends Server<Env> {
  onConnect(connection: Connection) {
    console.log(`[lobby:${this.name}] connected ${connection.id}`);
    connection.send(JSON.stringify({ type: "connected", playerId: this.name }));
  }

  onClose(connection: Connection) {
    console.log(`[lobby:${this.name}] disconnected ${connection.id}`);
  }

  async onRequest(request: Request): Promise<Response> {
    if (request.method !== "POST") {
      return new Response("method not allowed", { status: 405 });
    }
    const body = await request.text();
    console.log(`[lobby:${this.name}] push received: ${body}`);
    this.broadcast(body);
    return new Response("ok");
  }
}
