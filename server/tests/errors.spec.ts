import { Hono } from 'hono';
import { afterEach, expect, test, vi } from 'vitest';
import { InvalidPlayerCredentialsError } from '../src/errors';
import { createErrorHandler } from '../src/utils/errors';
import { mapDatabaseError } from '../src/db/errors';
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
  app.onError(createErrorHandler<{ Bindings: Env }>(mapDatabaseError));
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

test('the generic handler accepts another binding type and a caller-provided error mapping', async () => {
  const app = new Hono<{ Bindings: { validationMessage: string } }>();
  app.post('/custom', (c) => {
    throw new Error(c.env.validationMessage);
  });
  app.onError(
    createErrorHandler<{ Bindings: { validationMessage: string } }>((error) => new HTTPException(422, { message: error.message })),
  );
  const response = await app.request('/custom', { method: 'POST' }, { validationMessage: 'custom validation failed' });
  expect(response.status).toBe(422);
  expect(await response.json()).toEqual({ error: 'custom validation failed' });
});

test('the generic handler has no built-in membership or database rules', async () => {
  vi.spyOn(console, 'error').mockImplementation(() => {});
  const app = new Hono();
  app.post('/custom', () => {
    throw new Error('D1_ERROR: UNIQUE constraint failed: room_members.player_id: SQLITE_CONSTRAINT');
  });
  app.onError(createErrorHandler());
  const response = await app.request('/custom', { method: 'POST' });
  expect(response.status).toBe(500);
  expect(await response.json()).toEqual({ error: 'server error' });
});
