import { Hono } from 'hono';
import { cors } from 'hono/cors';
import { getServerByName, routePartykitRequest } from 'partyserver';
import matchmaking from './routes/matchmaking';
import { Lobby } from './party/lobby';
import { Room } from './party/room';
import type { Env } from './env';

// Durable Object classes must be exported from the worker's main module so
// wrangler can find them (see wrangler.jsonc durable_objects.bindings).
export { Lobby, Room };

const app = new Hono<{ Bindings: Env }>();
app.use('*', cors());
app.get('/health', (c) => c.text('ok'));
app.route('/api/matchmaking', matchmaking);
app.onError((error, c) => {
  console.error('request failed', error);
  return c.json({ error: 'server error' }, 500);
});

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const path = new URL(request.url).pathname;
    // Internal room control endpoints are reachable only through the binding.
    if (path.startsWith('/parties/') && request.headers.get('Upgrade')?.toLowerCase() !== 'websocket')
      return new Response('websocket upgrade required', { status: 426 });
    // WebSocket requests to /parties/lobby/rooms and
    // /parties/room/{id} are routed straight to the matching Durable Object.
    // Everything else falls through to the Hono REST API below.
    // This is all ONE Cloudflare Worker / ONE wrangler deploy - matchmaking
    // (Hono + D1) and signaling (partyserver Durable Objects) live together.
    const partyResponse = await routePartykitRequest(request, env, {
      async onBeforeConnect(_request, lobby) {
        // Initialize discovery when a browser connects, independently of room creation.
        // This persists the Lobby identity before its first WebSocket is accepted.
        if (lobby.className === 'Lobby') await getServerByName<Env, Lobby>(env.Lobby, lobby.name);
      },
    });
    if (partyResponse) return partyResponse;

    return app.fetch(request, env, ctx);
  },
} satisfies ExportedHandler<Env>;
