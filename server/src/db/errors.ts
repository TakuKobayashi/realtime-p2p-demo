import { getTableName } from 'drizzle-orm';
import { HTTPException } from 'hono/http-exception';
import { roomMembers } from './schema';

export function mapDatabaseError(error: Error): HTTPException | undefined {
  // Identify the one-room-per-player constraint from the schema, rather than duplicating SQL identifiers.
  const membershipColumn = `${getTableName(roomMembers)}.${roomMembers.playerId.name}`;
  // Drizzle may wrap the original D1 error in cause.
  const seen = new Set<Error>();
  let current: unknown = error;
  while (current instanceof Error && !seen.has(current)) {
    seen.add(current);
    const constraint = current.message.match(/UNIQUE constraint failed: ([\w.]+)/)?.[1];
    if (constraint === membershipColumn) {
      return new HTTPException(409, { message: 'leave the current room first' });
    }
    current = current.cause;
  }
  return undefined;
}
