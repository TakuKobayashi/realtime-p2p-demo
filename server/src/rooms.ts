import type { Env } from "./env";
import { getServerByName } from "partyserver";
import type { Room } from "./party/room";
import type { Lobby } from "./party/lobby";

export const LEASE_MS = 45_000;
export const ALARM_MS = 15_000;

export async function listRooms(db: D1Database, afterId = 0) {
  const rooms = await db.prepare(`SELECT r.id, r.max_players, r.created_at, COUNT(m.id) AS member_count
    FROM game_rooms r JOIN room_members m ON m.room_id = r.id AND m.expires_at > ?
    WHERE r.id > ? GROUP BY r.id ORDER BY r.id`).bind(Date.now(), afterId)
    .all<{ id: number; max_players: number | null; created_at: number; member_count: number }>();
  return rooms.results.map((room) => ({ id: String(room.id), maxPlayers: room.max_players ?? 0,
    memberCount: room.member_count, createdAt: room.created_at }));
}

export async function publishRooms(env: Env) {
  const lobby = await getServerByName<Env, Lobby>(env.Lobby, "rooms");
  const response = await lobby.fetch("https://internal/publish", {
    method: "POST", headers: { "x-partykit-room": "rooms" },
  });
  if (!response.ok) throw new Error("room discovery publication failed");
}

export function numericId(value: unknown): number | null {
  if (typeof value !== "string" || !/^[1-9]\d*$/.test(value)) return null;
  const id = Number(value);
  return Number.isSafeInteger(id) ? id : null;
}
export async function authenticate(db: D1Database, playerId: unknown, token: unknown): Promise<number | null> {
  const id = numericId(playerId);
  if (id === null || typeof token !== "string") return null;
  const player = await db.prepare("SELECT id FROM players WHERE id = ? AND token = ?")
    .bind(id, token).first<{ id: number }>();
  return player?.id ?? null;
}
export async function removeMember(db: D1Database, roomId: number, playerId: number, connectionId?: string) {
  // A stale socket must never delete a membership adopted by a newer socket.
  const condition = connectionId === undefined ? "" : " AND connection_id = ?";
  const bindings = connectionId === undefined ? [roomId, playerId] : [roomId, playerId, connectionId];
  const result = await db.batch([
    db.prepare(`DELETE FROM room_members WHERE room_id = ? AND player_id = ?${condition}`).bind(...bindings),
    db.prepare("DELETE FROM game_rooms WHERE id = ? AND NOT EXISTS (SELECT 1 FROM room_members WHERE room_id = ?)")
      .bind(roomId, roomId),
  ]);
  return result[0].meta.changes > 0;
}
export async function roomResponse(db: D1Database, roomId: number) {
  const room = await db.prepare(`SELECT r.id, r.max_players, r.created_at,
    (SELECT COUNT(*) FROM room_members m WHERE m.room_id = r.id AND m.expires_at > ?) AS member_count
    FROM game_rooms r WHERE r.id = ?`).bind(Date.now(), roomId)
    .first<{ id: number; max_players: number | null; created_at: number; member_count: number }>();
  if (!room) return null;
  return { id: String(room.id), maxPlayers: room.max_players ?? 0,
    memberCount: room.member_count, createdAt: room.created_at };
}
export async function controlRoom(env: Env, roomId: number, path: string, body?: string) {
  // partyserver persists its name fallback for hibernation on older workerd runtimes.
  const room = await getServerByName<Env, Room>(env.Room, String(roomId));
  return room.fetch(`https://internal/${path}`, {
    method: "POST", headers: { "x-partykit-room": String(roomId) }, body,
  });
}
