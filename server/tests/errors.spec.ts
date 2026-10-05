import { Hono } from 'hono';
import { afterEach, expect, test, vi } from 'vitest';
import { handleRequestError } from '../src/errors';
import type { Env } from '../src/env';

afterEach(() => vi.restoreAllMocks());

function failingApp(error: Error) {
  const app = new Hono<{ Bindings: Env }>();
  const routes = new Hono<{ Bindings: Env }>();
  routes.post('/rooms', () => {
    throw error;
  });
  routes.post('/rooms/:roomId/join', () => {
    throw error;
  });
  routes.post('/players', () => {
    throw error;
  });
  app.route('/api/matchmaking', routes);
  app.onError(handleRequestError);
  return app;
}

test.each([
  ['/rooms', 'leave the current room first'],
  ['/rooms/1/join', 'already joined a room'],
])('membership conflict on %s returns 409, including wrapped D1 errors', async (path, message) => {
  const original = new Error('D1_ERROR: UNIQUE constraint failed: room_members.player_id: SQLITE_CONSTRAINT');
  for (const error of [original, new Error('Failed query', { cause: original })]) {
    const response = await failingApp(error).request(`/api/matchmaking${path}`, { method: 'POST' });
    expect(response.status).toBe(409);
    expect(await response.json()).toEqual({ error: message });
  }
});

test.each([
  ['/rooms', new Error('D1_ERROR: UNIQUE constraint failed: players.token: SQLITE_CONSTRAINT')],
  ['/rooms', new Error('D1_ERROR: FOREIGN KEY constraint failed')],
  ['/rooms', new Error('could not schedule room cleanup')],
  ['/players', new Error('D1_ERROR: UNIQUE constraint failed: room_members.player_id: SQLITE_CONSTRAINT')],
])('unexpected error on %s is logged and returns 500', async (path, error) => {
  const log = vi.spyOn(console, 'error').mockImplementation(() => {});
  const response = await failingApp(error).request(`/api/matchmaking${path}`, { method: 'POST' });
  expect(response.status).toBe(500);
  expect(await response.json()).toEqual({ error: 'server error' });
  expect(log).toHaveBeenCalledWith('request failed', error);
});
