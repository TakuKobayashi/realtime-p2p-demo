import { sqliteTable, text, integer } from "drizzle-orm/sqlite-core";

/**
 * One row per player currently in (or recently matched out of) the queue.
 * "waiting"  -> still looking for an opponent.
 * "matched"  -> paired with opponentId in roomId; both rows are updated
 *               in the same request (see routes/matchmaking.ts).
 */
export const queuePlayers = sqliteTable("queue_players", {
  id: text("id").primaryKey(), // playerId, provided by the Unity client
  status: text("status", { enum: ["waiting", "matched"] })
    .notNull()
    .default("waiting"),
  roomId: text("room_id"),
  opponentId: text("opponent_id"),
  createdAt: integer("created_at", { mode: "timestamp" }).notNull(),
});

/** Public 1:1 room; SDP and ICE are still relayed by the Room Durable Object. */
export const gameRooms = sqliteTable("game_rooms", {
  id: text("id").primaryKey(),
  hostPlayerId: text("host_player_id").notNull(),
  guestPlayerId: text("guest_player_id"),
  status: text("status", { enum: ["waiting", "matched"] }).notNull().default("waiting"),
  createdAt: integer("created_at", { mode: "timestamp" }).notNull(),
});
