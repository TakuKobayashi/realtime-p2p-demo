import type { ErrorHandler } from 'hono';
import { HTTPException } from 'hono/http-exception';
import type { Env } from './env';

export class InvalidPlayerCredentialsError extends HTTPException {
  constructor() {
    super(401, { message: 'invalid player credentials' });
  }
}

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
  if (error instanceof HTTPException) {
    return c.json({ error: error.message }, error.status);
  }
  // The one-room-per-player constraint is a membership conflict regardless of the route.
  if (isMembershipConflict(error)) {
    return c.json({ error: 'leave the current room first' }, 409);
  }
  console.error('request failed', error);
  return c.json({ error: 'server error' }, 500);
};
