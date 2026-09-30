import { Hono } from "hono";
import { and, asc, eq, ne, sql } from "drizzle-orm";
import { createDb } from "../db/client";
import { gameRooms, queuePlayers } from "../db/schema";
import type { Env } from "../env";

const matchmaking = new Hono<{ Bindings: Env }>();

function roomResponse(room: { id: string; hostPlayerId: string; guestPlayerId: string | null; status: string; createdAt: Date }) {
  return {
    id: room.id,
    hostPlayerId: room.hostPlayerId,
    guestPlayerId: room.guestPlayerId,
    status: room.status,
    createdAt: room.createdAt.getTime(),
  };
}

/**
 * POST /api/matchmaking/join { playerId }
 *
 * 1v1 matchmaking:
 *  - If this player is already matched (idempotent retry), return the existing match.
 *  - Otherwise look for another "waiting" player (oldest first).
 *    - Found  -> create a roomId, mark BOTH players "matched", push a "matched"
 *                message straight into the OTHER player's Lobby Durable Object
 *                (same worker - see env.Lobby.idFromName/.get/.fetch below,
 *                 no separate signaling server to call out to), and return the
 *                match to the caller directly in this HTTP response.
 *    - None   -> insert self as "waiting" and return { status: "waiting" }.
 *
 * The player who *triggers* the match (the second one to call /join) is set as
 * isInitiator=false, and the player who was already waiting is isInitiator=true,
 * so exactly one side creates the WebRTC offer (avoids SDP glare).
 */
matchmaking.post("/join", async (c) => {
  const { playerId } = await c.req.json<{ playerId: string }>();
  if (!playerId) return c.json({ error: "playerId is required" }, 400);

  const db = createDb(c.env.DB);

  const existing = await db
    .select()
    .from(queuePlayers)
    .where(eq(queuePlayers.id, playerId))
    .get();

  if (existing?.status === "matched" && existing.roomId) {
    return c.json({
      status: "matched",
      roomId: existing.roomId,
      opponentId: existing.opponentId,
      isInitiator: false,
    });
  }

  const opponent = await db
    .select()
    .from(queuePlayers)
    .where(and(eq(queuePlayers.status, "waiting"), ne(queuePlayers.id, playerId)))
    .orderBy(asc(queuePlayers.createdAt))
    .limit(1)
    .get();

  if (!opponent) {
    await db
      .insert(queuePlayers)
      .values({ id: playerId, status: "waiting", createdAt: new Date() })
      .onConflictDoUpdate({
        target: queuePlayers.id,
        set: { status: "waiting", roomId: null, opponentId: null, createdAt: new Date() },
      });
    return c.json({ status: "waiting" });
  }

  const roomId = crypto.randomUUID();

  await db
    .update(queuePlayers)
    .set({ status: "matched", roomId, opponentId: playerId })
    .where(eq(queuePlayers.id, opponent.id));

  await db
    .insert(queuePlayers)
    .values({ id: playerId, status: "matched", roomId, opponentId: opponent.id, createdAt: new Date() })
    .onConflictDoUpdate({
      target: queuePlayers.id,
      set: { status: "matched", roomId, opponentId: opponent.id },
    });

  // Notify the opponent (who already returned from their own /join call and is
  // just idly connected to their Lobby room) by calling the Durable Object
  // directly - this is the SAME worker, so there's no second server involved.
  const lobbyId = c.env.Lobby.idFromName(opponent.id);
  const lobbyStub = c.env.Lobby.get(lobbyId);
  c.executionCtx.waitUntil(
    lobbyStub
      .fetch("https://internal/push", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          type: "matched",
          roomId,
          opponentId: playerId,
          isInitiator: true,
        }),
      })
      .catch((err) => console.error("lobby push failed", err))
  );

  return c.json({ status: "matched", roomId, opponentId: opponent.id, isInitiator: false });
});

matchmaking.post("/leave", async (c) => {
  const { playerId } = await c.req.json<{ playerId: string }>();
  if (!playerId) return c.json({ error: "playerId is required" }, 400);
  const db = createDb(c.env.DB);
  await db.delete(queuePlayers).where(eq(queuePlayers.id, playerId));
  await db.delete(gameRooms).where(sql`${gameRooms.hostPlayerId} = ${playerId} OR ${gameRooms.guestPlayerId} = ${playerId}`);
  return c.json({ status: "ok" });
});

/** Polling fallback in case the lobby websocket push is missed. */
matchmaking.get("/status/:playerId", async (c) => {
  const playerId = c.req.param("playerId");
  const db = createDb(c.env.DB);
  const row = await db.select().from(queuePlayers).where(eq(queuePlayers.id, playerId)).get();
  if (!row) return c.json({ status: "unknown" });
  return c.json({
    status: row.status,
    roomId: row.roomId ?? null,
    opponentId: row.opponentId ?? null,
  });
});

/** Create a public room. The creator is the WebRTC offerer. */
matchmaking.post("/rooms", async (c) => {
  const { playerId } = await c.req.json<{ playerId: string }>();
  if (!playerId) return c.json({ error: "playerId is required" }, 400);
  const db = createDb(c.env.DB);
  const existing = await db.select().from(gameRooms)
    .where(and(eq(gameRooms.hostPlayerId, playerId), eq(gameRooms.status, "waiting"))).get();
  if (existing) return c.json(roomResponse(existing));

  const room = {
    id: crypto.randomUUID(), hostPlayerId: playerId, guestPlayerId: null,
    status: "waiting" as const, createdAt: new Date(),
  };
  await db.insert(gameRooms).values(room);
  return c.json(roomResponse(room), 201);
});

/** List open rooms owned by other players. */
matchmaking.get("/rooms", async (c) => {
  const playerId = c.req.query("playerId");
  if (!playerId) return c.json({ error: "playerId is required" }, 400);
  const db = createDb(c.env.DB);
  const rooms = await db.select().from(gameRooms)
    .where(and(eq(gameRooms.status, "waiting"), ne(gameRooms.hostPlayerId, playerId)))
    .orderBy(asc(gameRooms.createdAt));
  return c.json(rooms.map(roomResponse));
});

/** Atomically reserve a room for its second player. */
matchmaking.post("/rooms/:roomId/join", async (c) => {
  const roomId = c.req.param("roomId");
  const { playerId } = await c.req.json<{ playerId: string }>();
  if (!playerId) return c.json({ error: "playerId is required" }, 400);
  const db = createDb(c.env.DB);
  await db.delete(gameRooms).where(and(eq(gameRooms.hostPlayerId, playerId), eq(gameRooms.status, "waiting")));
  const result = await c.env.DB.prepare(
    "UPDATE game_rooms SET status = 'matched', guest_player_id = ? WHERE id = ? AND status = 'waiting' AND host_player_id <> ?"
  ).bind(playerId, roomId, playerId).run();
  if (result.meta.changes !== 1) return c.json({ error: "room is no longer available" }, 409);
  const room = await db.select().from(gameRooms).where(eq(gameRooms.id, roomId)).get();
  return c.json({ ...roomResponse(room!), isInitiator: false });
});

export default matchmaking;
