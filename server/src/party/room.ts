import { Server, type Connection } from "partyserver";
import type { Env } from "../env";

/**
 * One Room Durable Object instance per matched 1v1 pair, keyed by roomId
 * (this.name === roomId). Reached by the Unity client at:
 *   wss://<worker-host>/parties/room/{roomId}
 *
 * Pure relay: whatever one peer sends (an "offer" / "answer" / "ice-candidate"
 * JSON envelope), the other peer receives verbatim. No parsing/validation is
 * done here on purpose - SDP/ICE payload shape is owned by the Unity client.
 */
export class Room extends Server<Env> {
  onConnect(connection: Connection) {
    const count = [...this.getConnections()].length;
    console.log(`[room:${this.name}] connected ${connection.id} (peers=${count})`);

    if (count > 2) {
      console.warn(`[room:${this.name}] rejecting ${connection.id}, room already has 2 peers (1v1 only)`);
      connection.close(4000, "room full");
      return;
    }

    connection.send(JSON.stringify({ type: "peer-count", count }));
  }

  onMessage(connection: Connection, message: string) {
    console.log(`[room:${this.name}] relay from ${connection.id}: ${message}`);
    for (const conn of this.getConnections()) {
      if (conn.id !== connection.id) {
        conn.send(message);
      }
    }
  }

  onClose(connection: Connection) {
    console.log(`[room:${this.name}] disconnected ${connection.id}`);
    for (const conn of this.getConnections()) {
      conn.send(JSON.stringify({ type: "peer-left" }));
    }
  }
}
