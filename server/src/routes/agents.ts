// routes/agents.ts - REST API for agents (v3 pure Node fs - no PowerShell spawn)
import { Router } from "express";
import path from "node:path";
import { fileURLToPath } from "node:url";
import fs from "node:fs";
import { db, queries, parseAgentRow } from "../db.js";
import { broadcast } from "../ws.js";

const router = Router();

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PROJECT_ROOT = path.resolve(__dirname, "..", "..", "..");
const AGENTS_DIR = path.join(process.env.USERPROFILE || "", ".trae-cn", "agents");
const SKILLS_DIR = path.join(process.env.USERPROFILE || "", ".trae-cn", "skills");

// ========== Helper: find agent source dir under PROJECT_ROOT ==========
function findAgentSourceDir(agentName: string): { agentMd: string; skillMd: string } | null {
  // Walk PROJECT_ROOT recursively looking for dirs containing `${agentName}\agent.md`
  function walk(dir: string, depth = 0): { agentMd: string; skillMd: string } | null {
    if (depth > 6) return null;
    let entries: fs.Dirent[];
    try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return null; }

    for (const e of entries) {
      if (!e.isDirectory()) continue;
      // Match dirs like "01_agent-name" or "agent-name"
      if (e.name.includes(agentName)) {
        const agentMd = path.join(dir, e.name, "agent.md");
        const skillMd = path.join(dir, e.name, "SKILL.md");
        if (fs.existsSync(agentMd)) return { agentMd, skillMd };
      }
      const found = walk(path.join(dir, e.name), depth + 1);
      if (found) return found;
    }
    return null;
  }
  return walk(PROJECT_ROOT);
}

// ========== Helper: broadcast install event ==========
function broadcastInstallEvent(name: string, status: string, detail?: string) {
  const data = { name, install_status: status, detail };
  queries.insertEvent.run(name, "install", JSON.stringify(data));
  broadcast({ type: "install", agent_name: name, data });
}

router.get("/", (_req, res) => {
  const rows = queries.getAll.all();
  const agents = rows.map(parseAgentRow);

  const summary = {
    total: agents.length,
    installed: agents.filter((a) => a.install_status === "installed").length,
    partial: agents.filter((a) => a.install_status === "partial").length,
    missing: agents.filter((a) => a.install_status !== "installed" && a.install_status !== "partial").length,
    busy: agents.filter((a) => a.runtime_status === "busy").length,
  };

  res.json({ summary, agents });
});

router.get("/:name", (req, res) => {
  const row = queries.getByName.get(req.params.name);
  if (!row) return res.status(404).json({ error: "not found" });
  res.json(parseAgentRow(row));
});

router.post("/:name/scan", (req, res) => {
  const { install_status, install_date } = req.body as {
    install_status?: string; install_date?: string;
  };
  const row = queries.getByName.get(req.params.name);
  if (!row) return res.status(404).json({ error: "agent not found" });

  queries.updateInstall.run(
    install_status ?? row.install_status,
    install_date ?? row.install_date,
    req.params.name
  );

  const data = {
    name: req.params.name,
    install_status: install_status ?? row.install_status,
    install_date: install_date ?? row.install_date,
  };
  queries.insertEvent.run(req.params.name, "scan", JSON.stringify(data));
  broadcast({ type: "scan", agent_name: req.params.name, data });

  res.json({ ok: true, updated: data });
});

router.post("/:name/status", (req, res) => {
  const { runtime_status } = req.body as { runtime_status?: string };
  if (!runtime_status) return res.status(400).json({ error: "runtime_status required" });

  const row = queries.getByName.get(req.params.name);
  if (!row) return res.status(404).json({ error: "agent not found" });

  queries.updateRuntime.run(runtime_status, req.params.name);

  const data = { name: req.params.name, runtime_status };
  queries.insertEvent.run(req.params.name, "runtime", JSON.stringify(data));
  broadcast({ type: "runtime", agent_name: req.params.name, data });

  res.json({ ok: true });
});

router.get("/:name/events", (req, res) => {
  const rows = db
    .prepare("SELECT * FROM status_events WHERE agent_name = ? ORDER BY id DESC LIMIT 50")
    .all(req.params.name);
  res.json(rows);
});

// ========== Z-tier: install management ==========

// POST /agents/:name/install - pure Node fs copy
router.post("/:name/install", async (req, res) => {
  const row = queries.getByName.get(req.params.name);
  if (!row) return res.status(404).json({ error: "agent not found" });

  // mark busy
  queries.updateRuntime.run("busy", req.params.name);
  broadcast({ type: "runtime", agent_name: req.params.name, data: { runtime_status: "busy" } });

  const src = findAgentSourceDir(req.params.name);
  if (!src) {
    queries.updateRuntime.run("idle", req.params.name);
    broadcast({ type: "runtime", agent_name: req.params.name, data: { runtime_status: "idle" } });
    broadcastInstallEvent(req.params.name, row.install_status, "source directory not found");
    return res.json({ ok: false, error: "source not found", install_status: row.install_status });
  }

  let copied: string[] = [];
  let errors: string[] = [];

  try { fs.mkdirSync(AGENTS_DIR, { recursive: true }); } catch (e: any) { errors.push(`mkdir agents: ${e.message}`); }
  try { fs.mkdirSync(path.join(SKILLS_DIR, req.params.name), { recursive: true }); } catch (e: any) { errors.push(`mkdir skill: ${e.message}`); }

  // copy agent.md
  try {
    const target = path.join(AGENTS_DIR, `${req.params.name}.md`);
    fs.copyFileSync(src.agentMd, target);
    copied.push(target);
  } catch (e: any) { errors.push(`agent.md: ${e.message}`); }

  // copy SKILL.md if exists
  if (fs.existsSync(src.skillMd)) {
    try {
      const target = path.join(SKILLS_DIR, req.params.name, "SKILL.md");
      fs.copyFileSync(src.skillMd, target);
      copied.push(target);
    } catch (e: any) { errors.push(`SKILL.md: ${e.message}`); }
  }

  const newStatus = copied.length > 0 ? "installed" : row.install_status;
  queries.updateInstall.run(newStatus, new Date().toISOString().slice(0, 10), req.params.name);
  queries.updateRuntime.run("idle", req.params.name);

  broadcastInstallEvent(req.params.name, newStatus, copied.length ? `copied ${copied.length} files` : errors.join("; "));
  broadcast({ type: "runtime", agent_name: req.params.name, data: { runtime_status: "idle" } });

  res.json({ ok: copied.length > 0, copied, errors, install_status: newStatus });
});

// POST /agents/:name/uninstall - pure Node fs delete
router.post("/:name/uninstall", (req, res) => {
  const row = queries.getByName.get(req.params.name);
  if (!row) return res.status(404).json({ error: "agent not found" });

  const agentMd = path.join(AGENTS_DIR, `${req.params.name}.md`);
  const skillMd = path.join(SKILLS_DIR, req.params.name, "SKILL.md");
  const skillDir = path.join(SKILLS_DIR, req.params.name);

  let deleted: string[] = [];
  let errors: string[] = [];

  try { if (fs.existsSync(agentMd)) { fs.unlinkSync(agentMd); deleted.push(agentMd); } } catch (e: any) { errors.push(`agent.md: ${e.message}`); }
  try { if (fs.existsSync(skillMd)) { fs.unlinkSync(skillMd); deleted.push(skillMd); } } catch (e: any) { errors.push(`SKILL.md: ${e.message}`); }
  try { if (fs.existsSync(skillDir)) { fs.rmSync(skillDir, { recursive: true, force: true }); deleted.push(skillDir + "(dir)"); } } catch (e: any) { errors.push(`skill dir: ${e.message}`); }

  queries.updateInstall.run("not-installed", null, req.params.name);
  broadcastInstallEvent(req.params.name, "not-installed", errors.length ? errors.join("; ") : `uninstalled ${deleted.length} files`);

  res.json({ ok: errors.length === 0 || deleted.length > 0, deleted, errors, install_status: "not-installed" });
});

// POST /agents/scan-all - pure Node fs walk, update all DB records
router.post("/scan-all", (_req, res) => {
  const allAgents = queries.getAll.all().map(parseAgentRow);
  let updated = 0;
  let installed = 0, partial = 0, missing = 0;

  for (const a of allAgents) {
    const agentMd = path.join(AGENTS_DIR, `${a.name}.md`);
    const skillMd = path.join(SKILLS_DIR, a.name, "SKILL.md");
    const hasAgent = fs.existsSync(agentMd);
    const hasSkill = fs.existsSync(skillMd);

    let status: string;
    if (hasAgent && hasSkill) status = "installed";
    else if (hasAgent || hasSkill) status = "partial";
    else status = "not-installed";

    if (a.install_status !== status) {
      queries.updateInstall.run(status, null, a.name);
      broadcastInstallEvent(a.name, status, "scan updated");
      updated++;
    }

    if (status === "installed") installed++;
    else if (status === "partial") partial++;
    else missing++;
  }

  res.json({ ok: true, total: allAgents.length, installed, partial, missing, updated });
});

// POST /agents/install-all - loop install
router.post("/install-all", async (_req, res) => {
  const allAgents = queries.getAll.all().map(parseAgentRow);
  const results: any[] = [];
  let ok = 0, fail = 0;

  for (const a of allAgents) {
    const src = findAgentSourceDir(a.name);
    if (!src) { results.push({ name: a.name, ok: false, reason: "source not found" }); fail++; continue; }

    try { fs.mkdirSync(AGENTS_DIR, { recursive: true }); } catch {}
    try { fs.mkdirSync(path.join(SKILLS_DIR, a.name), { recursive: true }); } catch {}

    const targetAgent = path.join(AGENTS_DIR, `${a.name}.md`);
    const targetSkill = path.join(SKILLS_DIR, a.name, "SKILL.md");
    let copied = 0;
    try { fs.copyFileSync(src.agentMd, targetAgent); copied++; } catch {}
    if (fs.existsSync(src.skillMd)) { try { fs.copyFileSync(src.skillMd, targetSkill); copied++; } catch {} }

    const newStatus = copied > 0 ? "installed" : a.install_status;
    queries.updateInstall.run(newStatus, new Date().toISOString().slice(0, 10), a.name);
    broadcastInstallEvent(a.name, newStatus, `bulk install: ${copied} files`);
    results.push({ name: a.name, ok: copied > 0, copied, install_status: newStatus });
    copied > 0 ? ok++ : fail++;
  }

  res.json({ ok: true, total: allAgents.length, ok, fail, results });
});

// ========== A-tier: CRUD with governance (管制协议 5 条门槛) ==========

interface CreateAgentBody {
  name: string;
  cn_name: string;
  priority: "P0" | "P1";
  emoji?: string;
  desc?: string;
  role?: string;
  how_to_trigger?: { manual: string[]; auto: string[] };
  process?: string[];
  boundary?: string[];
  tools?: string[];
  depends_on?: string[];
}

// Governance pre-check: 5 hard gates
function runGovernanceCheck(body: CreateAgentBody, excludeName?: string): {
  pass: boolean;
  gates: { id: number; name: string; pass: boolean; detail?: string }[];
} {
  const gates: { id: number; name: string; pass: boolean; detail?: string }[] = [];

  // Gate 1: English name uniqueness
  const name = body.name.trim().toLowerCase();
  let nameConflict = false;
  const dbRow = queries.getByName.get(name);
  if (dbRow && dbRow.name !== excludeName) nameConflict = true;
  // Also check source dirs
  if (!nameConflict && findAgentSourceDir(name)) nameConflict = true;
  gates.push({
    id: 1, name: "英文名唯一",
    pass: !nameConflict,
    detail: nameConflict ? `重名：${name} 已存在（DB 或源目录）` : "OK",
  });

  // Gate 2: No capability overlap (soft conflict — warn only, don't block)
  // We compare role desc with existing agents' role + tools
  const roleText = (body.role || "").toLowerCase();
  const allAgents = queries.getAll.all().map(parseAgentRow);
  const overlaps: string[] = [];
  for (const a of allAgents) {
    if (excludeName && a.name === excludeName) continue;
    const aRole = (a.role || "").toLowerCase();
    const sharedTools = (body.tools || []).filter((t) => a.tools.includes(t)).length;
    if (sharedTools >= 2 && roleText.length > 0 && aRole.length > 0) {
      // crude overlap: if share tools AND role keywords collide
      const keywords = roleText.split(/[\s,，。、；;]+/).filter((w) => w.length >= 2);
      const collides = keywords.filter((k) => aRole.includes(k)).length;
      if (collides >= 2) overlaps.push(`${a.cn_name}(${a.name}) — 共享工具 ${sharedTools} 个 + 角色关键词撞 ${collides} 个`);
    }
  }
  gates.push({
    id: 2, name: "能力补位（无抢活）",
    pass: overlaps.length === 0,
    detail: overlaps.length ? overlaps.join("; ") : "OK（未检测到抢活）",
  });

  // Gate 3: All required detail fields present
  const requiredOk =
    body.role && body.role.trim().length >= 10 &&
    body.how_to_trigger?.manual?.length >= 1 &&
    body.how_to_trigger?.auto?.length >= 1 &&
    body.boundary?.length >= 1;
  gates.push({
    id: 3, name: "有完整 Skill 支撑（role + trigger + boundary）",
    pass: !!requiredOk,
    detail: requiredOk ? "OK" : "role<10字 或 trigger/boundary 为空",
  });

  // Gate 4: Priority valid
  const priorityOk = body.priority === "P0" || body.priority === "P1";
  gates.push({
    id: 4, name: "优先级合法（P0/P1）",
    pass: priorityOk,
    detail: priorityOk ? "OK" : `非法优先级：${body.priority}`,
  });

  // Gate 5: Name format valid (kebab-case)
  const nameFormatOk = /^[a-z][a-z0-9-]*[a-z0-9]$/.test(name);
  gates.push({
    id: 5, name: "英文名格式合法（kebab-case）",
    pass: nameFormatOk,
    detail: nameFormatOk ? "OK" : `英文名必须小写+连字符：${name}`,
  });

  const allPass = gates.every((g) => g.pass);
  return { pass: allPass, gates };
}

// POST /agents - create new agent (with governance)
router.post("/", (req, res) => {
  const body = req.body as CreateAgentBody;
  if (!body?.name || !body?.cn_name) {
    return res.status(400).json({ error: "name + cn_name required" });
  }

  const check = runGovernanceCheck(body);
  if (!check.pass) {
    return res.status(422).json({
      ok: false,
      error: "governance check failed",
      gates: check.gates,
    });
  }

  // Next ID
  const all = queries.getAll.all();
  const nextId = all.length > 0 ? Math.max(...all.map((a: any) => a.id)) + 1 : 1;

  const priorityOrder = { P0: 1, P1: 2 };
  const lastP0 = all.filter((a: any) => a.priority === "P0").length;
  const lastP1 = all.filter((a: any) => a.priority === "P1").length;
  const nextNum = body.priority === "P0" ? lastP0 + 1 : lastP1 + 1;
  const dirPrefix = `${String(nextNum).padStart(2, "0")}_`;

  // Create source directory with agent.md + SKILL.md
  const agentsSrcRoot = findAgentSourceDir(body.name) ? path.dirname(path.dirname(findAgentSourceDir(body.name)!.agentMd)) : null;
  let baseDir: string;
  if (agentsSrcRoot) {
    // Append new dir next to existing
    baseDir = path.join(path.dirname(agentsSrcRoot), `${dirPrefix}${body.name}`);
  } else {
    // Fallback: create in PROJECT_ROOT/专岗工作台/02_AGENTS/
    baseDir = path.join(PROJECT_ROOT, "专岗工作台", "02_AGENTS", `${dirPrefix}${body.name}`);
  }
  fs.mkdirSync(baseDir, { recursive: true });

  // Generate agent.md content
  const agentMd = `# ${body.cn_name}（${body.name}）

- Priority: ${body.priority}
- Emoji: ${body.emoji || "🤖"}
- Desc: ${body.desc || ""}

## Role
${body.role || ""}

## Manual Triggers
${(body.how_to_trigger?.manual || []).map((t) => `- "${t}"`).join("\n")}

## Auto Triggers
${(body.how_to_trigger?.auto || []).map((t) => `- ${t}`).join("\n")}

## Process
${(body.process || []).map((p) => `- ${p}`).join("\n")}

## Boundary
${(body.boundary || []).map((b) => `- ${b}`).join("\n")}

## Tools
${(body.tools || []).join(", ")}

## Depends On
${(body.depends_on || []).join(", ") || "(none)"}
`;

  const skillMd = `# SKILL: ${body.name}

## Description
${body.role || body.desc || ""}

## When to Use
${(body.how_to_trigger?.auto || []).join("\n")}
`;

  const agentMdPath = path.join(baseDir, "agent.md");
  const skillMdPath = path.join(baseDir, "SKILL.md");
  fs.writeFileSync(agentMdPath, agentMd, "utf8");
  fs.writeFileSync(skillMdPath, skillMd, "utf8");

  // Insert into DB
  const stmt = db.prepare(`
    INSERT INTO agents (id, name, cn_name, priority, emoji, desc, role, how_to_trigger, process, boundary, tools, depends_on, updated_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'))
  `);
  stmt.run(
    nextId,
    body.name.toLowerCase(),
    body.cn_name,
    body.priority,
    body.emoji || "🤖",
    body.desc || "",
    body.role || "",
    JSON.stringify(body.how_to_trigger || { manual: [], auto: [] }),
    JSON.stringify(body.process || []),
    JSON.stringify(body.boundary || []),
    JSON.stringify(body.tools || []),
    JSON.stringify(body.depends_on || []),
  );

  queries.insertEvent.run(body.name.toLowerCase(), "create", JSON.stringify({ dir: baseDir }));
  broadcast({
    type: "install",
    agent_name: body.name.toLowerCase(),
    data: { install_status: "not-installed", detail: "agent created in source" },
  });

  res.status(201).json({
    ok: true,
    gates: check.gates,
    agentMd: agentMdPath,
    skillMd: skillMdPath,
  });
});

// PUT /agents/:name - update agent fields
router.put("/:name", (req, res) => {
  const row = queries.getByName.get(req.params.name);
  if (!row) return res.status(404).json({ error: "agent not found" });

  const b = req.body as any;
  const sets: string[] = [];
  const vals: any[] = [];

  const fields = ["cn_name", "priority", "emoji", "desc", "role"];
  for (const f of fields) {
    if (b[f] !== undefined) { sets.push(`${f} = ?`); vals.push(b[f]); }
  }
  if (b.how_to_trigger) { sets.push("how_to_trigger = ?"); vals.push(JSON.stringify(b.how_to_trigger)); }
  if (b.process) { sets.push("process = ?"); vals.push(JSON.stringify(b.process)); }
  if (b.boundary) { sets.push("boundary = ?"); vals.push(JSON.stringify(b.boundary)); }
  if (b.tools) { sets.push("tools = ?"); vals.push(JSON.stringify(b.tools)); }
  if (b.depends_on) { sets.push("depends_on = ?"); vals.push(JSON.stringify(b.depends_on)); }

  if (sets.length === 0) return res.status(400).json({ error: "no fields to update" });

  sets.push("updated_at = datetime('now')");
  vals.push(req.params.name);

  db.prepare(`UPDATE agents SET ${sets.join(", ")} WHERE name = ?`).run(...vals);

  // Also sync to agent.md if source dir exists
  const src = findAgentSourceDir(req.params.name);
  if (src) {
    const fresh = queries.getByName.get(req.params.name);
    if (fresh) {
      const a = parseAgentRow(fresh);
      const agentMd = `# ${a.cn_name}（${a.name}）

- Priority: ${a.priority}
- Emoji: ${a.emoji || "🤖"}
- Desc: ${a.desc || ""}

## Role
${a.role || ""}

## Manual Triggers
${(a.how_to_trigger?.manual || []).map((t: string) => `- "${t}"`).join("\n")}

## Auto Triggers
${(a.how_to_trigger?.auto || []).map((t: string) => `- ${t}`).join("\n")}

## Process
${(a.process || []).map((p: string) => `- ${p}`).join("\n")}

## Boundary
${(a.boundary || []).map((b: string) => `- ${b}`).join("\n")}

## Tools
${(a.tools || []).join(", ")}

## Depends On
${(a.depends_on || []).join(", ") || "(none)"}
`;
      try { fs.writeFileSync(src.agentMd, agentMd, "utf8"); } catch {}
    }
  }

  res.json({ ok: true });
});

// DELETE /agents/:name - delete agent (DB + source dir)
router.delete("/:name", (req, res) => {
  const row = queries.getByName.get(req.params.name);
  if (!row) return res.status(404).json({ error: "agent not found" });

  const deleted: string[] = [];
  const errors: string[] = [];

  // Delete source dir
  const src = findAgentSourceDir(req.params.name);
  if (src) {
    const dir = path.dirname(src.agentMd);
    try {
      fs.rmSync(dir, { recursive: true, force: true });
      deleted.push(dir + "(source)");
    } catch (e: any) { errors.push(`source dir: ${e.message}`); }
  }

  // Delete installed files
  const agentMd = path.join(AGENTS_DIR, `${req.params.name}.md`);
  const skillDir = path.join(SKILLS_DIR, req.params.name);
  try { if (fs.existsSync(agentMd)) { fs.unlinkSync(agentMd); deleted.push(agentMd); } } catch {}
  try { if (fs.existsSync(skillDir)) { fs.rmSync(skillDir, { recursive: true, force: true }); deleted.push(skillDir + "(skill)"); } } catch {}

  // Delete from DB
  db.prepare("DELETE FROM agents WHERE name = ?").run(req.params.name);

  queries.insertEvent.run(req.params.name, "delete", JSON.stringify({ deleted }));
  broadcast({ type: "install", agent_name: req.params.name, data: { install_status: "deleted", detail: "agent removed" } });

  res.json({ ok: true, deleted, errors });
});

export default router;
