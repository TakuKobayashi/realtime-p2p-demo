import { Hono } from 'hono';
import { afterEach, expect, test, vi } from 'vitest';
import { handleRequestError, InvalidPlayerCredentialsError } from '../src/errors';
import { HTTPException } from 'hono/http-exception';
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
  ['/rooms/1/join', 'leave the current room first'],
  ['/players', 'leave the current room first'],
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
])('unexpected error on %s is logged and returns 500', async (path, error) => {
  const log = vi.spyOn(console, 'error').mockImplementation(() => {});
  const response = await failingApp(error).request(`/api/matchmaking${path}`, { method: 'POST' });
  expect(response.status).toBe(500);
  expect(await response.json()).toEqual({ error: 'server error' });
  expect(log).toHaveBeenCalledWith('request failed', error);
});

test.each(['/rooms', '/rooms/1/join', '/players'])('authentication error on %s returns the common 401 response', async (path) => {
  const response = await failingApp(new InvalidPlayerCredentialsError()).request(`/api/matchmaking${path}`, { method: 'POST' });
  expect(response.status).toBe(401);
  expect(await response.json()).toEqual({ error: 'invalid player credentials' });
});

test('HTTP errors preserve their status and JSON message', async () => {
  const response = await failingApp(new HTTPException(400, { message: 'invalid room id' })).request('/api/matchmaking/rooms', {
    method: 'POST',
  });
  expect(response.status).toBe(400);
  expect(await response.json()).toEqual({ error: 'invalid room id' });
});
