CREATE TABLE game_rooms (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  max_players INTEGER CHECK (max_players IS NULL OR max_players > 0),
  created_at INTEGER NOT NULL
);
CREATE TABLE room_members (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  room_id INTEGER NOT NULL REFERENCES game_rooms(id) ON DELETE CASCADE,
  player_id INTEGER NOT NULL REFERENCES players(id),
  connection_id TEXT,
  expires_at INTEGER NOT NULL
);
CREATE UNIQUE INDEX room_members_player ON room_members(player_id);
CREATE INDEX room_members_room_expiry ON room_members(room_id, expires_at);
