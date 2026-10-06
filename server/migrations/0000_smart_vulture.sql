CREATE TABLE `game_rooms` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`max_players` integer,
	`created_at` integer NOT NULL
);
--> statement-breakpoint
CREATE TABLE `players` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`token` text NOT NULL,
	`created_at` integer NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `players_token_unique` ON `players` (`token`);--> statement-breakpoint
CREATE TABLE `room_members` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`room_id` integer NOT NULL,
	`player_id` integer NOT NULL,
	`connection_id` text,
	`expires_at` integer NOT NULL,
	FOREIGN KEY (`room_id`) REFERENCES `game_rooms`(`id`) ON UPDATE no action ON DELETE cascade,
	FOREIGN KEY (`player_id`) REFERENCES `players`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE UNIQUE INDEX `room_members_player` ON `room_members` (`player_id`);--> statement-breakpoint
CREATE INDEX `room_members_room_expiry` ON `room_members` (`room_id`,`expires_at`);
--> statement-breakpoint
CREATE INDEX `room_members_expiry` ON `room_members` (`expires_at`);
