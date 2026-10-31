// ws.ts - WebSocket broadcast manager
import { WebSocketServer, WebSocket } from "ws";

export let wss: WebSocketServer | null = null;
const clients = new Set<WebSocket>();

export function initWs(server: any) {
  wss = new WebSocketServer({ server, path: "/events" });

  wss.on("connection", (ws) => {
    clients.add(ws);
    console.log("[ws] client connected, total:", clients.size);

    // Send a hello + current snapshot
    ws.send(JSON.stringify({
      type: "hello",
      ts: new Date().toISOString(),
      msg: "connected to agent-workbench"
    }));

    ws.on("close", () => {
      clients.delete(ws);
      console.log("[ws] client disconnected, total:", clients.size);
    });
  });

  console.log("[ws] WebSocket server ready at /events");
}

export function broadcast(event: { type: string; agent_name?: string; data?: any }) {
  if (!wss || clients.size === 0) return;
  const msg = JSON.stringify({ ...event, ts: new Date().toISOString() });
  for (const c of clients) {
    if (c.readyState === WebSocket.OPEN) c.send(msg);
  }
}
