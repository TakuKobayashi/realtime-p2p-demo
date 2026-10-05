import type { Env, ErrorHandler } from 'hono';
import { HTTPException } from 'hono/http-exception';

export function createErrorHandler<E extends Env>(mapError?: (error: Error) => HTTPException | undefined): ErrorHandler<E> {
  return (error, c) => {
    const httpError = error instanceof HTTPException ? error : mapError?.(error);
    if (httpError) return c.json({ error: httpError.message }, httpError.status);
    console.error('request failed', error);
    return c.json({ error: 'server error' }, 500);
  };
}
