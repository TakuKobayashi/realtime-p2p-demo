import type { ErrorHandler } from 'hono';
import { routePath } from 'hono/route';
import type { Env } from './env';

function isMembershipConflict(error: Error): boolean {
  // Drizzle may wrap the original D1 error in cause.
  const seen = new Set<Error>();
  let current: unknown = error;
  while (current instanceof Error && !seen.has(current)) {
    seen.add(current);
    if (/UNIQUE constraint failed: room_members\.player_id\b/.test(current.message)) return true;
    current = current.cause;
  }
  return false;
}

export const handleRequestError: ErrorHandler<{ Bindings: Env }> = (error, c) => {
  const path = routePath(c);
  if (c.req.method === 'POST' && isMembershipConflict(error)) {
    if (path === '/api/matchmaking/rooms') return c.json({ error: 'leave the current room first' }, 409);
    if (path === '/api/matchmaking/rooms/:roomId/join') return c.json({ error: 'already joined a room' }, 409);
  }
  console.error('request failed', error);
  return c.json({ error: 'server error' }, 500);
};
