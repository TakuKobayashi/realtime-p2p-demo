// @ts-expect-error Node 22 provides node:sqlite, but the installed type definitions do not yet declare it.
import { DatabaseSync } from 'node:sqlite';
import { readdirSync } from 'node:fs';
import { resolve } from 'node:path';

const directory = resolve(process.cwd(), '.wrangler', 'state', 'v3', 'd1', 'miniflare-D1DatabaseObject');
const files = readdirSync(directory).filter((name) => name.endsWith('.sqlite') && name !== 'metadata.sqlite');
if (files.length !== 1) throw new Error('Could not identify the local D1 SQLite database.');
const database = new DatabaseSync(resolve(directory, files[0]));
try {
  const tables = database
    .prepare("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '_cf_%'")
    .all()
    .map((row: { name: string }) => row.name);
  database.exec('PRAGMA foreign_keys = OFF;');
  for (const name of tables) database.exec(`DROP TABLE IF EXISTS "${name.replaceAll('"', '""')}";`);
  database.exec('PRAGMA foreign_keys = ON;');
} finally {
  database.close();
}
