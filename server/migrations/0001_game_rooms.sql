CREATE TABLE `game_rooms` (
  `id` text PRIMARY KEY NOT NULL,
  `host_player_id` text NOT NULL,
  `guest_player_id` text,
  `status` text DEFAULT 'waiting' NOT NULL,
  `created_at` integer NOT NULL
);

CREATE INDEX `game_rooms_waiting_created_at` ON `game_rooms` (`status`, `created_at`);
