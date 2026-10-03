import { Hono } from 'hono';
import type { Env } from '../env';
import { authenticate, controlRoom, LEASE_MS, listRooms, numericId, publishRooms, roomResponse } from '../rooms';

const matchmaking = new Hono<{ Bindings: Env }>();
matchmaking.post('/players', async (c) => {
  const token = crypto.randomUUID();
  const player = await c.env.DB.prepare('INSERT INTO players (token, created_at) VALUES (?, ?) RETURNING id')
    .bind(token, Date.now())
    .first<{ id: number }>();
  return c.json({ id: String(player!.id), token }, 201);
});
matchmaking.post('/rooms', async (c) => {
  const body = await c.req.json<{ playerId?: string; token?: string; maxPlayers?: number }>();
  const playerId = await authenticate(c.env.DB, body.playerId, body.token);
  if (playerId === null) return c.json({ error: 'invalid player credentials' }, 401);
  const maxPlayers = body.maxPlayers ?? 0;
  if (!Number.isSafeInteger(maxPlayers) || maxPlayers < 0)
    return c.json({ error: 'maxPlayers must be a non-negative integer (0 = unlimited)' }, 400);
  let results: D1Result[];
  try {
    // Transaction: creator reservation and new room either both exist or neither does.
    results = await c.env.DB.batch([
      c.env.DB.prepare('DELETE FROM room_members WHERE player_id = ? AND expires_at <= ?').bind(playerId, Date.now()),
      c.env.DB.prepare('INSERT INTO game_rooms (max_players, created_at) VALUES (?, ?) RETURNING id').bind(
        maxPlayers === 0 ? null : maxPlayers,
        Date.now(),
      ),
      c.env.DB.prepare('INSERT INTO room_members (room_id, player_id, expires_at) VALUES (last_insert_rowid(), ?, ?)').bind(
        playerId,
        Date.now() + LEASE_MS,
      ),
    ]);
  } catch (error) {
    if (String(error).includes('UNIQUE constraint failed')) return c.json({ error: 'leave the current room first' }, 409);
    throw error;
  }
  const roomId = (results[1].results[0] as { id: number }).id;
  const scheduled = await controlRoom(c.env, roomId, 'schedule');
  if (!scheduled.ok) throw new Error('could not schedule room cleanup');
  c.executionCtx.waitUntil(publishRooms(c.env).catch((error) => console.error('room publication failed', error)));
  return c.json(await roomResponse(c.env.DB, roomId), 201);
});
matchmaking.get('/rooms', async (c) => {
  return c.json(await listRooms(c.env.DB));
});
matchmaking.post('/rooms/:roomId/join', async (c) => {
  const roomId = numericId(c.req.param('roomId'));
  if (roomId === null) return c.json({ error: 'invalid room id' }, 400);
  const body = await c.req.json<{ playerId?: string; token?: string }>();
  const playerId = await authenticate(c.env.DB, body.playerId, body.token);
  if (playerId === null) return c.json({ error: 'invalid player credentials' }, 401);
  const now = Date.now();
  const existing = await c.env.DB.prepare('SELECT room_id FROM room_members WHERE player_id = ? AND expires_at > ?')
    .bind(playerId, now)
    .first<{ room_id: number }>();
  if (existing) {
    if (existing.room_id === roomId) return c.json(await roomResponse(c.env.DB, roomId));
    return c.json({ error: 'leave the current room first' }, 409);
  }
  let results: D1Result[];
  try {
    results = await c.env.DB.batch([
      c.env.DB.prepare('DELETE FROM room_members WHERE player_id = ? AND expires_at <= ?').bind(playerId, now),
      // Capacity check and admission are ONE atomic SQL statement.
      c.env.DB.prepare(
        `INSERT INTO room_members (room_id, player_id, expires_at)
        SELECT id, ?, ? FROM game_rooms WHERE id = ?
        AND EXISTS (SELECT 1 FROM room_members WHERE room_id = ? AND expires_at > ?)
        AND (max_players IS NULL OR
          (SELECT COUNT(*) FROM room_members WHERE room_id = ? AND expires_at > ?) < max_players)`,
      ).bind(playerId, now + LEASE_MS, roomId, roomId, now, roomId, now),
    ]);
  } catch (error) {
    if (String(error).includes('UNIQUE constraint failed')) return c.json({ error: 'already joined a room' }, 409);
    throw error;
  }
  if (results[1].meta.changes !== 1) return c.json({ error: 'room is full or no longer available' }, 409);
  const scheduled = await controlRoom(c.env, roomId, 'schedule');
  if (!scheduled.ok) throw new Error('could not schedule room cleanup');
  return c.json(await roomResponse(c.env.DB, roomId));
});
matchmaking.post('/rooms/:roomId/leave', async (c) => {
  const roomId = numericId(c.req.param('roomId'));
  if (roomId === null) return c.json({ error: 'invalid room id' }, 400);
  const body = await c.req.json<{ playerId?: string; token?: string }>();
  const playerId = await authenticate(c.env.DB, body.playerId, body.token);
  if (playerId === null) return c.json({ error: 'invalid player credentials' }, 401);
  return controlRoom(c.env, roomId, 'leave', JSON.stringify({ playerId }));
});
// Room discovery replaces the old pair queue. Client and server upgrade together.
matchmaking.post('/join', (c) => c.json({ error: 'use /rooms to create or join a multiplayer room' }, 410));
matchmaking.post('/leave', (c) => c.json({ error: 'use /rooms/:roomId/leave' }, 410));
export default matchmaking;
