// api/agents.ts - REST API client
export interface Agent {
  id: number;
  name: string;
  cn_name: string;
  priority: "P0" | "P1";
  emoji: string;
  desc: string;
  role: string;
  how_to_trigger: { manual: string[]; auto: string[] };
  process: string[];
  boundary: string[];
  tools: string[];
  install_status: "installed" | "partial" | "not-installed";
  install_date?: string | null;
  runtime_status: "idle" | "busy";
  last_seen?: string | null;
  depends_on: string[];
  updated_at?: string;
}

export interface AgentListResponse {
  summary: {
    total: number;
    installed: number;
    partial: number;
    missing: number;
    busy: number;
  };
  agents: Agent[];
}

const API = "/api/agents";

export async function fetchAgents(): Promise<AgentListResponse> {
  const r = await fetch(API);
  if (!r.ok) throw new Error(`GET /agents ${r.status}`);
  return r.json();
}

export async function fetchAgent(name: string): Promise<Agent> {
  const r = await fetch(`${API}/${name}`);
  if (!r.ok) throw new Error(`GET /agents/${name} ${r.status}`);
  return r.json();
}

async function postJSON(url: string, body?: any) {
  const r = await fetch(url, {
    method: "POST",
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!r.ok) throw new Error(`${r.status}: ${await r.text()}`);
  return r.json();
}

// Z-tier install management APIs
export async function installAgent(name: string) {
  return postJSON(`${API}/${name}/install`);
}

export async function uninstallAgent(name: string) {
  return postJSON(`${API}/${name}/uninstall`);
}

export async function scanAllAgents() {
  return postJSON(`${API}/scan-all`);
}

export async function installAllAgents() {
  return postJSON(`${API}/install-all`);
}

export async function setAgentStatus(name: string, runtime_status: "idle" | "busy") {
  return postJSON(`${API}/${name}/status`, { runtime_status });
}

export async function scanAgent(name: string, install_status: string) {
  return postJSON(`${API}/${name}/scan`, { install_status });
}

export interface CreateAgentPayload {
  name: string; cn_name: string; priority: "P0" | "P1"; emoji?: string;
  desc?: string; role?: string;
  how_to_trigger?: { manual: string[]; auto: string[] };
  process?: string[]; boundary?: string[]; tools?: string[]; depends_on?: string[];
}

export interface GateCheck { id: number; name: string; pass: boolean; detail?: string; }

export async function createAgent(body: CreateAgentPayload): Promise<{ ok: boolean; gates?: GateCheck[]; agentMd?: string; skillMd?: string; error?: string }> {
  const r = await fetch(API, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const data = await r.json();
  if (!r.ok) {
    if (r.status === 422) return { ok: false, gates: data.gates, error: data.error };
    return { ok: false, error: data.error || String(r.status) };
  }
  return { ok: true, gates: data.gates, agentMd: data.agentMd, skillMd: data.skillMd };
}

export async function updateAgent(name: string, body: Partial<CreateAgentPayload>): Promise<{ ok: boolean }> {
  const r = await fetch(`${API}/${name}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!r.ok) return { ok: false, error: await r.text() } as any;
  return r.json();
}

export async function deleteAgent(name: string): Promise<{ ok: boolean; deleted: string[]; errors: string[] }> {
  const r = await fetch(`${API}/${name}`, { method: "DELETE" });
  return r.json();
}
