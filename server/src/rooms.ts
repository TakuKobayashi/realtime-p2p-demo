import { and, count, eq, gt, notExists } from 'drizzle-orm';
import { createDb, type Db } from './db/client';
import { gameRooms, players, roomMembers } from './db/schema';
import type { Env } from './env';
import { getServerByName } from 'partyserver';
import type { Room } from './party/room';
import type { Lobby } from './party/lobby';

export const LEASE_MS = 45_000;
export const ALARM_MS = 15_000;

export async function listRooms(d1: D1Database, afterId = 0) {
  const db = createDb(d1);
  const rooms = await db
    .select({ id: gameRooms.id, maxPlayers: gameRooms.maxPlayers, createdAt: gameRooms.createdAt, memberCount: count(roomMembers.id) })
    .from(gameRooms)
    .innerJoin(roomMembers, and(eq(roomMembers.roomId, gameRooms.id), gt(roomMembers.expiresAt, Date.now())))
    .where(gt(gameRooms.id, afterId))
    .groupBy(gameRooms.id)
    .orderBy(gameRooms.id);
  return rooms.map((room) => ({ ...room, maxPlayers: room.maxPlayers ?? 0 }));
}

export async function publishRooms(env: Env) {
  const lobby = await getServerByName<Env, Lobby>(env.Lobby, 'rooms');
  await lobby.publishRooms();
}

export function numericId(value: unknown): number | null {
  if (typeof value !== 'string' || !/^[1-9]\d*$/.test(value)) return null;
  const id = Number(value);
  return Number.isSafeInteger(id) ? id : null;
}

export async function authenticate(d1: D1Database, playerId: unknown, token: unknown): Promise<number | null> {
  const id = numericId(playerId);
  if (id === null || typeof token !== 'string') return null;
  const player = await createDb(d1)
    .select({ id: players.id })
    .from(players)
    .where(and(eq(players.id, id), eq(players.token, token)))
    .get();
  return player?.id ?? null;
}

export function deleteEmptyRoom(db: Db, roomId: number) {
  return db
    .delete(gameRooms)
    .where(
      and(
        eq(gameRooms.id, roomId),
        notExists(db.select({ id: roomMembers.id }).from(roomMembers).where(eq(roomMembers.roomId, gameRooms.id))),
      ),
    );
}

export async function removeMember(d1: D1Database, roomId: number, playerId: number, connectionId?: string) {
  const db = createDb(d1);
  // A stale socket must never delete a membership adopted by a newer socket.
  const result = await db.batch([
    db
      .delete(roomMembers)
      .where(
        and(
          eq(roomMembers.roomId, roomId),
          eq(roomMembers.playerId, playerId),
          connectionId === undefined ? undefined : eq(roomMembers.connectionId, connectionId),
        ),
      ),
    deleteEmptyRoom(db, roomId),
  ]);
  return result[0].meta.changes > 0;
}

export async function roomResponse(d1: D1Database, roomId: number) {
  const db = createDb(d1);
  const room = await db
    .select({ id: gameRooms.id, maxPlayers: gameRooms.maxPlayers, createdAt: gameRooms.createdAt, memberCount: count(roomMembers.id) })
    .from(gameRooms)
    .leftJoin(roomMembers, and(eq(roomMembers.roomId, gameRooms.id), gt(roomMembers.expiresAt, Date.now())))
    .where(eq(gameRooms.id, roomId))
    .groupBy(gameRooms.id)
    .get();
  if (!room) return null;
  return { ...room, maxPlayers: room.maxPlayers ?? 0 };
}

export function getRoom(env: Env, roomId: number) {
  return getServerByName<Env, Room>(env.Room, String(roomId));
}
