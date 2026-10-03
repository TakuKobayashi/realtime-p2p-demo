/**
 * Bindings for the single "realtime-p2p-server" Cloudflare Worker.
 * DB    -> D1, used by the Hono matchmaking REST API.
 * Lobby -> Durable Object namespace, room discovery stream (see party/lobby.ts).
 * Room  -> Durable Object namespace, one instance per multiplayer roomId (see party/room.ts).
 */
import type { Room } from "./party/room";
import type { Lobby } from "./party/lobby";

export type Env = {
  DB: D1Database;
  Lobby: DurableObjectNamespace<Lobby>;
  Room: DurableObjectNamespace<Room>;
};
