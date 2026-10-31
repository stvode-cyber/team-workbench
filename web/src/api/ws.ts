// api/ws.ts - WebSocket client (auto-reconnect)
import type { Agent } from "./agents";

type EventType = "scan" | "runtime" | "install" | "hello";
export interface WsEvent {
  type: EventType;
  agent_name?: string;
  data?: any;
  ts: string;
}

type OnScan = (agentName: string, data: any) => void;
type OnRuntime = (agentName: string, runtimeStatus: string) => void;
type OnInstall = (agentName: string, data: any) => void;

let ws: WebSocket | null = null;
let reconnectTimer: number | null = null;

export function connectWs(onScan: OnScan, onRuntime: OnRuntime, onInstall?: OnInstall) {
  const proto = location.protocol === "https:" ? "wss:" : "ws:";
  const url = `${proto}//${location.host}/events`;

  ws = new WebSocket(url);

  ws.onmessage = (ev) => {
    try {
      const msg: WsEvent = JSON.parse(ev.data);
      if (msg.type === "scan" && msg.agent_name) {
        onScan(msg.agent_name, msg.data);
      } else if (msg.type === "runtime" && msg.agent_name && msg.data?.runtime_status) {
        onRuntime(msg.agent_name, msg.data.runtime_status);
      } else if (msg.type === "install" && msg.agent_name) {
        onInstall?.(msg.agent_name, msg.data);
      }
    } catch { /* ignore */ }
  };

  ws.onclose = () => {
    if (reconnectTimer) clearTimeout(reconnectTimer);
    reconnectTimer = window.setTimeout(() => connectWs(onScan, onRuntime, onInstall), 3000);
  };
}

export function closeWs() {
  if (ws) ws.close();
  if (reconnectTimer) clearTimeout(reconnectTimer);
}
