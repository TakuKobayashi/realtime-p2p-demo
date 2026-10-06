import { Hono } from 'hono';
import { HTTPException } from 'hono/http-exception';
import { and, count, eq, exists, gt, isNull, lt, lte, or, sql } from 'drizzle-orm';
import { createDb } from '../db/client';
import { gameRooms, players, roomMembers } from '../db/schema';
import type { Env } from '../env';
import { getSignaling, LEASE_MS, listRooms, numericId, publishRooms, requirePlayer, roomResponse } from '../rooms';

const matchmaking = new Hono<{ Bindings: Env }>();
matchmaking.post('/players', async (c) => {
  const token = crypto.randomUUID();
  const player = await createDb(c.env.DB).insert(players).values({ token, createdAt: Date.now() }).returning({ id: players.id }).get();
  return c.json({ id: String(player.id), token }, 201);
});

matchmaking.post('/rooms', async (c) => {
  const body = await c.req.json<{ playerId?: string; token?: string; maxPlayers?: number }>();
  const playerId = await requirePlayer(c.env.DB, body.playerId, body.token);
  const maxPlayers = body.maxPlayers ?? 0;
  if (!Number.isSafeInteger(maxPlayers) || maxPlayers < 0) {
    throw new HTTPException(400, { message: 'maxPlayers must be a non-negative integer (0 = unlimited)' });
  }
  const db = createDb(c.env.DB);
  const now = Date.now();
  // D1 batch is transactional: creator reservation and room both exist or neither does.
  const results = await db.batch([
    db.delete(roomMembers).where(and(eq(roomMembers.playerId, playerId), lte(roomMembers.expiresAt, now))),
    db
      .insert(gameRooms)
      .values({ maxPlayers: maxPlayers === 0 ? null : maxPlayers, createdAt: now })
      .returning({ id: gameRooms.id }),
    // SQLite's generated ID is consumed inside the same batch, before another insert.
    db.insert(roomMembers).values({ roomId: sql<number>`last_insert_rowid()`, playerId, expiresAt: now + LEASE_MS }),
  ]);
  const roomId = results[1][0].id;
  const room = await getSignaling(c.env);
  await room.scheduleCleanup();
  c.executionCtx.waitUntil(publishRooms(c.env).catch((error) => console.error('room publication failed', error)));
  return c.json(await roomResponse(c.env.DB, roomId), 201);
});

matchmaking.get('/rooms', async (c) => {
  const rooms = await listRooms(c.env.DB);
  return c.json(rooms);
});

matchmaking.post('/rooms/:roomId/join', async (c) => {
  const roomId = numericId(c.req.param('roomId'));
  if (roomId === null) throw new HTTPException(400, { message: 'invalid room id' });
  const body = await c.req.json<{ playerId?: string; token?: string }>();
  const playerId = await requirePlayer(c.env.DB, body.playerId, body.token);
  const now = Date.now();
  const db = createDb(c.env.DB);
  const existing = await db
    .select({ roomId: roomMembers.roomId })
    .from(roomMembers)
    .where(and(eq(roomMembers.playerId, playerId), gt(roomMembers.expiresAt, now)))
    .get();
  if (existing) {
    if (existing.roomId === roomId) {
      return c.json(await roomResponse(c.env.DB, roomId));
    }
    throw new HTTPException(409, { message: 'leave the current room first' });
  }
  const activeMembers = db
    .select({ memberCount: count() })
    .from(roomMembers)
    .where(and(eq(roomMembers.roomId, gameRooms.id), gt(roomMembers.expiresAt, now)));
  const liveMember = db
    .select({ id: roomMembers.id })
    .from(roomMembers)
    .where(and(eq(roomMembers.roomId, gameRooms.id), gt(roomMembers.expiresAt, now)));
  const results = await db.batch([
    db.delete(roomMembers).where(and(eq(roomMembers.playerId, playerId), lte(roomMembers.expiresAt, now))),
    // Keep capacity check and admission in one atomic INSERT ... SELECT.
    db.insert(roomMembers).select(
      db
        .select({
          id: sql<null>`null`.as('id'),
          roomId: gameRooms.id,
          playerId: sql<number>`${playerId}`.as('player_id'),
          connectionId: sql<null>`null`.as('connection_id'),
          expiresAt: sql<number>`${now + LEASE_MS}`.as('expires_at'),
        })
        .from(gameRooms)
        .where(
          and(eq(gameRooms.id, roomId), exists(liveMember), or(isNull(gameRooms.maxPlayers), lt(activeMembers, gameRooms.maxPlayers))),
        ),
    ),
  ]);
  if (results[1].meta.changes !== 1) {
    throw new HTTPException(409, { message: 'room is full or no longer available' });
  }
  const room = await getSignaling(c.env);
  await room.scheduleCleanup();
  return c.json(await roomResponse(c.env.DB, roomId));
});

matchmaking.post('/rooms/:roomId/leave', async (c) => {
  const roomId = numericId(c.req.param('roomId'));
  if (roomId === null) {
    throw new HTTPException(400, { message: 'invalid room id' });
  }
  const body = await c.req.json<{ playerId?: string; token?: string }>();
  const playerId = await requirePlayer(c.env.DB, body.playerId, body.token);
  const room = await getSignaling(c.env);
  await room.leave(roomId, playerId);
  return c.json({ status: 'ok' });
});

export default matchmaking;
