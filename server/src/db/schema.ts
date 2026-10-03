import { sqliteTable, text, integer, uniqueIndex, index } from "drizzle-orm/sqlite-core";

// IDs are database-issued opaque integers. Tokens are credentials, not IDs.
export const players = sqliteTable("players", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  token: text("token").notNull().unique(),
  createdAt: integer("created_at").notNull(),
});
export const gameRooms = sqliteTable("game_rooms", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  maxPlayers: integer("max_players"), // null = no application-imposed limit
  createdAt: integer("created_at").notNull(),
});
export const roomMembers = sqliteTable("room_members", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  roomId: integer("room_id").notNull().references(() => gameRooms.id, { onDelete: "cascade" }),
  playerId: integer("player_id").notNull().references(() => players.id),
  connectionId: text("connection_id"),
  expiresAt: integer("expires_at").notNull(),
}, (table) => ({
  playerRoom: uniqueIndex("room_members_player").on(table.playerId),
  roomExpiry: index("room_members_room_expiry").on(table.roomId, table.expiresAt),
}));
