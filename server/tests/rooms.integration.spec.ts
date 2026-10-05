import { assert, test } from "vitest";
import { execFileSync, spawn, type ChildProcess } from "node:child_process";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { setTimeout as delay } from "node:timers/promises";

type Player = { id: string; token: string };
type Room = { id: number; maxPlayers: number; memberCount: number; createdAt: number };
type RequestBody = Record<string, string | number>;
type MessagePayloads = {
  "rooms-created": { rooms: Room[] };
  subscribed: {};
  pong: {};
  "room-joined": { peers: string[]; isInitiator: boolean };
  "peer-joined": { from: string };
  "peer-left": { from: string };
  offer: { from: string; sdp: string };
  answer: { from: string; sdp: string };
  "ice-candidate": { from: string; candidate: string };
  heartbeat: {};
};
type MessageType = keyof MessagePayloads;
type Message<K extends MessageType = MessageType> = {
  [T in K]: { type: T } & MessagePayloads[T]
}[K];

// A fresh local workerd + D1 database, never the deployed Worker or developer DB.
const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const wrangler = join(root, "node_modules/wrangler/bin/wrangler.js");
test("room discovery, admission, mesh signaling and membership lifecycle", async () => {
  const state = await mkdtemp(join(tmpdir(), "p2p-rooms-integration-"));
  const port = Number(process.env.P2P_TEST_PORT ?? 8797);
  const base = `http://127.0.0.1:${port}`;
  const sockets: WebSocket[] = [];
  let worker: ChildProcess | undefined;
  let output = "";
  function api(path: "/players", body: RequestBody, expected?: number): Promise<Player>;
  function api(path: "/rooms", body?: undefined, expected?: number): Promise<Room[]>;
  function api(path: "/rooms", body: RequestBody, expected?: number): Promise<Room>;
  function api(path: string, body?: RequestBody, expected?: number): Promise<unknown>;
  async function api(path: string, body?: RequestBody, expected = 200): Promise<unknown> {
    const response = await fetch(`${base}/api/matchmaking${path}`, {
      method: body === undefined ? "GET" : "POST",
      headers: { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const payload = await response.text();
    const result = payload.startsWith("{") || payload.startsWith("[") ? JSON.parse(payload) : { error: payload };
    assert.strictEqual(response.status, expected, JSON.stringify(result));
    return result;
  }
  const credentials = (player: Player) => ({ playerId: player.id, token: player.token });
  async function discover(lastRoomId: number) {
    const ws = new WebSocket(`ws://127.0.0.1:${port}/parties/lobby/rooms`);
    sockets.push(ws);
    const messages: Message[] = [];
    ws.addEventListener("message", (event) => messages.push(JSON.parse(String(event.data)) as Message));
    await new Promise<void>((yes, no) => { ws.addEventListener("open", () => yes(), { once: true }); ws.addEventListener("error", no, { once: true }); });
    const wait = async <K extends MessageType>(type: K): Promise<Message<K>> => {
      for (let i = 0; i < 100; i++) {
        const index = messages.findIndex((m) => m.type === type);
        if (index >= 0) return messages.splice(index, 1)[0] as Message<K>;
        await delay(50);
      }
      throw new Error(`No discovery ${type} received`);
    };
    ws.send(JSON.stringify({ type: "subscribe", lastRoomId }));
    await wait("subscribed");
    return { ws, messages, wait };
  }
  async function connect(roomId: number, player: Player) {
    const ws = new WebSocket(`ws://127.0.0.1:${port}/parties/room/${roomId}?playerId=${player.id}&token=${player.token}`);
    sockets.push(ws);
    const messages: Message[] = [];
    ws.addEventListener("message", (event) => messages.push(JSON.parse(String(event.data)) as Message));
    await new Promise<void>((yes, no) => { ws.addEventListener("open", () => yes(), { once: true }); ws.addEventListener("error", no, { once: true }); });
    const wait = async <K extends MessageType>(type: K, predicate: (message: Message<K>) => boolean = () => true): Promise<Message<K>> => {
      for (let i = 0; i < 100; i++) {
        const index = messages.findIndex((m) => m.type === type && predicate(m as Message<K>));
        if (index >= 0) return messages.splice(index, 1)[0] as Message<K>;
        await delay(50);
      }
      throw new Error(`No ${type} received for player ${player.id}`);
    };
    ws.send(JSON.stringify({ type: "client-ready" }));
    const roster = await wait("room-joined");
    return { ws, messages, wait, roster };
  }
  async function until(check: () => Promise<boolean>) {
    for (let i = 0; i < 100; i++) { if (await check()) return; await delay(50); }
    throw new Error("Condition not met");
  }

  try {
    execFileSync(process.execPath, [wrangler, "d1", "migrations", "apply", "realtime-p2p-db", "--local", "--persist-to", state],
      { cwd: root, windowsHide: true, input: "y\n", env: { ...process.env, CI: "true", WRANGLER_SEND_METRICS: "false" }, stdio: ["pipe", "pipe", "pipe"] });
    worker = spawn(process.execPath, [wrangler, "dev", "--local", "--port", String(port), "--persist-to", state],
      { cwd: root, windowsHide: true, env: { ...process.env, WRANGLER_SEND_METRICS: "false" }, stdio: ["ignore", "pipe", "pipe"] });
    worker.stdout!.on("data", (chunk) => { output += chunk; });
    worker.stderr!.on("data", (chunk) => { output += chunk; });
    let ready = false;
    for (let i = 0; i < 100; i++) {
      if (worker.exitCode !== null) throw new Error(output);
      try { ready = (await fetch(`${base}/health`)).ok; } catch { /* Wait for local startup. */ }
      if (ready) break;
      await delay(250);
    }
    assert.ok(ready, output);

    // No room has ever been created: HTTP returns [] and the lobby can subscribe.
    assert.deepEqual(await api("/rooms"), []);
    const coldBrowser = await discover(0);
    assert.strictEqual(coldBrowser.messages.length, 0);
    const emptyReconnect = await discover(0);
    assert.strictEqual(emptyReconnect.messages.length, 0);
    emptyReconnect.ws.close();

    const players: Player[] = [];
    for (let i = 0; i < 8; i++) players.push(await api("/players", {}, 201));
    assert.deepEqual(players.map((p) => p.id), ["1", "2", "3", "4", "5", "6", "7", "8"]);
    const [a, b, c, d] = players;
    await api("/rooms", { ...credentials(a), token: "invalid", maxPlayers: 3 }, 401);
    await api("/rooms", { ...credentials(a), maxPlayers: -1 }, 400);
    const room = await api("/rooms", { ...credentials(a), maxPlayers: 3 }, 201);
    assert.strictEqual(room.id, 1);
    assert.strictEqual(room.memberCount, 1); // Creator is listed before anyone else arrives.
    // A conflicting creator reservation rolls the room insert back as well.
    await api("/rooms", { ...credentials(a), maxPlayers: 3 }, 409);
    assert.deepEqual((await api("/rooms")).map((r) => r.id), [room.id]);
    await api(`/rooms/999999/join`, credentials(d), 409);
    assert.deepEqual((await coldBrowser.wait("rooms-created")).rooms.map((r) => r.id), [room.id]);
    coldBrowser.ws.close();
    assert.strictEqual((await api("/rooms"))[0].id, room.id);
    const browser = await discover(room.id);
    await delay(100);
    assert.strictEqual(browser.messages.length, 0); // HTTP snapshot rooms are never resent.
    // Valid JSON above the removed 1024-character lobby limit still receives a response.
    browser.ws.send(JSON.stringify({ type: "ping", padding: "x".repeat(1024 + 1) }));
    await browser.wait("pong");
    const sa = await connect(room.id, a);
    assert.deepEqual(sa.roster.peers, []); // No wait for second participant.
    await api(`/rooms/${room.id}/join`, credentials(b));
    await api(`/rooms/${room.id}/join`, credentials(b)); // Joining the same room is idempotent.
    const sb = await connect(room.id, b);
    assert.deepEqual(sb.roster.peers, [a.id]);
    assert.strictEqual(sb.roster.isInitiator, true); // Role is explicit, never derived from ID.
    assert.strictEqual((await sa.wait("peer-joined")).from, b.id);
    await api(`/rooms/${room.id}/join`, credentials(c));
    const sc = await connect(room.id, c);
    assert.deepEqual(new Set(sc.roster.peers), new Set([a.id, b.id]));
    await sa.wait("peer-joined", (m) => m.from === c.id);
    await sb.wait("peer-joined", (m) => m.from === c.id);
    await api(`/rooms/${room.id}/join`, credentials(d), 409);
    assert.strictEqual((await api("/rooms"))[0].memberCount, 3);
    // Regression: relay a payload above the removed 128 * 1024-character limit.
    const largeOffer = "targeted-test-offer".padEnd(128 * 1024 + 1, "x");
    sa.ws.send(JSON.stringify({ type: "offer", from: c.id, to: b.id, sdp: largeOffer }));
    const offer = await sb.wait("offer");
    assert.strictEqual(offer.from, a.id); // Spoofed source is overwritten.
    assert.strictEqual(offer.sdp, largeOffer); // Relay preserves the entire payload.
    await delay(150);
    assert.ok(!sc.messages.some((m) => m.type === "offer")); // No cross-pair SDP broadcast.
    sb.ws.send(JSON.stringify({ type: "answer", to: a.id, sdp: "answer" }));
    assert.strictEqual((await sa.wait("answer")).from, b.id);
    sc.ws.send(JSON.stringify({ type: "ice-candidate", to: b.id, candidate: "candidate:test", sdpMid: "0", sdpMLineIndex: 0 }));
    assert.strictEqual((await sb.wait("ice-candidate")).from, c.id);
    sa.ws.send(JSON.stringify({ type: "heartbeat" }));
    await sa.wait("heartbeat");

    // Replacing a socket cannot let its delayed onClose delete the new membership.
    const replacement = await connect(room.id, a);
    await sb.wait("peer-left", (m) => m.from === a.id);
    await sb.wait("peer-joined", (m) => m.from === a.id);
    assert.deepEqual(new Set(replacement.roster.peers), new Set([b.id, c.id]));
    await delay(100);
    assert.strictEqual((await api("/rooms"))[0].memberCount, 3);

    await api(`/rooms/${room.id}/leave`, credentials(a)); // Creator does not own the room's lifetime.
    await sb.wait("peer-left", (m) => m.from === a.id);
    assert.strictEqual((await api("/rooms"))[0].memberCount, 2);
    sc.ws.close(); // Abrupt socket exit does not require HTTP leave.
    await sb.wait("peer-left", (m) => m.from === c.id);
    await until(async () => (await api("/rooms"))[0]?.memberCount === 1);
    await api(`/rooms/${room.id}/leave`, credentials(b));
    assert.deepEqual(await api("/rooms"), []);

    // No fixed two-player limit: admit five in an unlimited room.
    const unlimited = await api("/rooms", { ...credentials(a), maxPlayers: 0 }, 201);
    assert.strictEqual(unlimited.id, 2); // Deleted IDs are not reused.
    assert.deepEqual((await browser.wait("rooms-created")).rooms.map((r) => r.id), [unlimited.id]);
    // The room created between HTTP and socket subscription is caught up exactly once.
    const catchUp = await discover(room.id);
    assert.deepEqual((await catchUp.wait("rooms-created")).rooms.map((r) => r.id), [unlimited.id]);
    browser.ws.close();
    catchUp.ws.close();
    for (const player of players.slice(1, 5)) await api(`/rooms/${unlimited.id}/join`, credentials(player));
    assert.strictEqual((await api("/rooms"))[0].memberCount, 5);
    for (const player of players.slice(0, 5)) await api(`/rooms/${unlimited.id}/leave`, credentials(player));

    // Simultaneous joins cannot exceed the last available slot.
    const limited = await api("/rooms", { ...credentials(a), maxPlayers: 2 }, 201);
    const reconnected = await discover(unlimited.id);
    assert.deepEqual((await reconnected.wait("rooms-created")).rooms.map((r) => r.id), [limited.id]);
    const joins = await Promise.all([b, c, d].map((p) => fetch(`${base}/api/matchmaking/rooms/${limited.id}/join`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(credentials(p)),
    })));
    assert.deepEqual(joins.map((r) => r.status).sort(), [200, 409, 409]);
    for (const player of [a, b, c, d]) await api(`/rooms/${limited.id}/leave`, credentials(player));
    assert.deepEqual(await api("/rooms"), []);

    // Newness is a numeric comparison, including RoomId 9 -> 10.
    for (let id = 4; id <= 12; id++) {
      const next = await api("/rooms", { ...credentials(a), maxPlayers: 0 }, 201);
      assert.strictEqual(next.id, id);
      assert.deepEqual((await reconnected.wait("rooms-created")).rooms.map((r) => r.id), [id]);
      await api(`/rooms/${next.id}/leave`, credentials(a));
    }

    // No socket ever opened: reservation expiry + alarm must physically delete the room.
    const abandoned = await api("/rooms", { ...credentials(a), maxPlayers: 0 }, 201);
    const silentRoom = await api("/rooms", { ...credentials(players[5]), maxPlayers: 0 }, 201);
    const created = new Set<number>();
    while (created.size < 2) {
      for (const room of (await reconnected.wait("rooms-created")).rooms) {
        assert.ok(!created.has(room.id), "new room must not be duplicated");
        created.add(room.id);
      }
    }
    assert.deepEqual(created, new Set([abandoned.id, silentRoom.id]));
    const latest = await discover(silentRoom.id);
    assert.strictEqual(latest.messages.length, 0);
    const emptyCursor = await discover(0);
    assert.deepEqual((await emptyCursor.wait("rooms-created")).rooms.map((r) => r.id), [abandoned.id, silentRoom.id]);
    await delay(100);
    assert.strictEqual(reconnected.messages.length, 0);
    const silent = await connect(silentRoom.id, players[5]);
    execFileSync(process.execPath, [wrangler, "d1", "execute", "realtime-p2p-db", "--local", "--persist-to", state,
      "--command", `UPDATE room_members SET expires_at = 0 WHERE room_id IN (${abandoned.id}, ${silentRoom.id})`],
      { cwd: root, windowsHide: true, env: { ...process.env, WRANGLER_SEND_METRICS: "false" }, stdio: "pipe" });
    await delay(16_000);
    const verify = execFileSync(process.execPath, [wrangler, "d1", "execute", "realtime-p2p-db", "--local", "--persist-to", state,
      "--command", "SELECT COUNT(*) AS remaining FROM game_rooms", "--json"],
      { cwd: root, windowsHide: true, env: { ...process.env, WRANGLER_SEND_METRICS: "false" }, stdio: "pipe" }).toString();
    assert.strictEqual(JSON.parse(verify).at(-1).results[0].remaining, 0);
    await until(async () => silent.ws.readyState === WebSocket.CLOSED);
  } catch (error) {
    await delay(250);
    console.error(output.slice(-10000));
    throw error;
  } finally {
    for (const ws of sockets) if (ws.readyState === WebSocket.OPEN) ws.close();
    if (worker) {
      const stopped = new Promise<void>((yes) => worker!.once("exit", () => yes()));
      worker.kill();
      await Promise.race([stopped, delay(3000)]);
    }
    // Only the directory returned by mkdtemp is eligible for cleanup.
    await rm(state, { recursive: true, force: true, maxRetries: 10, retryDelay: 250 });
  }
}, 120_000);
