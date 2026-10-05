import { Hono } from 'hono';
import { and, count, eq, exists, gt, isNull, lt, lte, or, sql } from 'drizzle-orm';
import { createDb } from '../db/client';
import { gameRooms, players, roomMembers } from '../db/schema';
import type { Env } from '../env';
import { authenticate, controlRoom, LEASE_MS, listRooms, numericId, publishRooms, roomResponse } from '../rooms';

const matchmaking = new Hono<{ Bindings: Env }>();
matchmaking.post('/players', async (c) => {
  const token = crypto.randomUUID();
  const player = await createDb(c.env.DB).insert(players).values({ token, createdAt: Date.now() }).returning({ id: players.id }).get();
  return c.json({ id: String(player.id), token }, 201);
});

matchmaking.post('/rooms', async (c) => {
  const body = await c.req.json<{ playerId?: string; token?: string; maxPlayers?: number }>();
  const playerId = await authenticate(c.env.DB, body.playerId, body.token);
  if (playerId === null) return c.json({ error: 'invalid player credentials' }, 401);
  const maxPlayers = body.maxPlayers ?? 0;
  if (!Number.isSafeInteger(maxPlayers) || maxPlayers < 0)
    return c.json({ error: 'maxPlayers must be a non-negative integer (0 = unlimited)' }, 400);
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
  const scheduled = await controlRoom(c.env, roomId, 'schedule');
  if (!scheduled.ok) throw new Error('could not schedule room cleanup');
  c.executionCtx.waitUntil(publishRooms(c.env).catch((error) => console.error('room publication failed', error)));
  return c.json(await roomResponse(c.env.DB, roomId), 201);
});

matchmaking.get('/rooms', async (c) => {
  const rooms = await listRooms(c.env.DB);
  return c.json(rooms);
});

matchmaking.post('/rooms/:roomId/join', async (c) => {
  const roomId = numericId(c.req.param('roomId'));
  if (roomId === null) return c.json({ error: 'invalid room id' }, 400);
  const body = await c.req.json<{ playerId?: string; token?: string }>();
  const playerId = await authenticate(c.env.DB, body.playerId, body.token);
  if (playerId === null) return c.json({ error: 'invalid player credentials' }, 401);
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
    return c.json({ error: 'leave the current room first' }, 409);
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
    return c.json({ error: 'room is full or no longer available' }, 409);
  }
  const scheduled = await controlRoom(c.env, roomId, 'schedule');
  if (!scheduled.ok) {
    throw new Error('could not schedule room cleanup');
  }
  return c.json(await roomResponse(c.env.DB, roomId));
});

matchmaking.post('/rooms/:roomId/leave', async (c) => {
  const roomId = numericId(c.req.param('roomId'));
  if (roomId === null) {
    return c.json({ error: 'invalid room id' }, 400);
  }
  const body = await c.req.json<{ playerId?: string; token?: string }>();
  const playerId = await authenticate(c.env.DB, body.playerId, body.token);
  if (playerId === null) {
    return c.json({ error: 'invalid player credentials' }, 401);
  }
  return controlRoom(c.env, roomId, 'leave', JSON.stringify({ playerId }));
});

export default matchmaking;
