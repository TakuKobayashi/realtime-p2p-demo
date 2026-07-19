/**
 * Bindings for the single "realtime-p2p-server" Cloudflare Worker.
 * DB    -> D1, used by the Hono matchmaking REST API.
 * Lobby -> Durable Object namespace, one instance per playerId (see party/lobby.ts).
 * Room  -> Durable Object namespace, one instance per matched roomId (see party/room.ts).
 */
export type Env = {
  DB: D1Database;
  Lobby: DurableObjectNamespace;
  Room: DurableObjectNamespace;
};
