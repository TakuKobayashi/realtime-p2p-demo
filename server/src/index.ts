import { Hono } from 'hono';
import { cors } from 'hono/cors';
import { getServerByName } from 'partyserver';
import matchmaking from './routes/matchmaking';
import { Lobby } from './party/lobby';
import { Signaling } from './party/signaling';
import type { Env } from './env';
import { getSignaling } from './rooms';
import { createErrorHandler } from './utils/errors';
import { mapDatabaseError } from './db/errors';

export { Lobby, Signaling };
const app = new Hono<{ Bindings: Env }>();
app.use('/api/*', cors());
app.use('/health', cors());
app.get('/health', (c) => c.text('ok'));
app.route('/api/matchmaking', matchmaking);
app.onError(createErrorHandler<{ Bindings: Env }>(mapDatabaseError));

app.get('/signaling', async (c) => {
  if (c.req.header('Upgrade')?.toLowerCase() !== 'websocket') return c.text('websocket upgrade required', 426);
  const signaling = await getSignaling(c.env);
  return signaling.fetch(c.req.raw);
});
app.get('/parties/lobby/rooms', async (c) => {
  if (c.req.header('Upgrade')?.toLowerCase() !== 'websocket') return c.text('websocket upgrade required', 426);
  const lobby = await getServerByName<Env, Lobby>(c.env.Lobby, 'rooms');
  return lobby.fetch(c.req.raw);
});

export default app;
