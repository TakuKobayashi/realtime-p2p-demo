import { Hono } from "hono";
import { cors } from "hono/cors";
import { routePartykitRequest } from "partyserver";
import matchmaking from "./routes/matchmaking";
import { Lobby } from "./party/lobby";
import { Room } from "./party/room";
import type { Env } from "./env";

// Durable Object classes must be exported from the worker's main module so
// wrangler can find them (see wrangler.jsonc durable_objects.bindings).
export { Lobby, Room };

const app = new Hono<{ Bindings: Env }>();
app.use("*", cors());
app.get("/health", (c) => c.text("ok"));
app.route("/api/matchmaking", matchmaking);

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    // WebSocket (and HTTP push) requests to /parties/lobby/{id} and
    // /parties/room/{id} are routed straight to the matching Durable Object.
    // Everything else falls through to the Hono REST API below.
    // This is all ONE Cloudflare Worker / ONE wrangler deploy - matchmaking
    // (Hono + D1) and signaling (partyserver Durable Objects) live together.
    const partyResponse = await routePartykitRequest(request, env);
    if (partyResponse) return partyResponse;

    return app.fetch(request, env, ctx);
  },
} satisfies ExportedHandler<Env>;
