// db.ts - SQLite connection + schema + seed + migration layer (C方案 · 不删库)
import Database from "better-sqlite3";
import path from "node:path";
import { fileURLToPath } from "node:url";
import fs from "node:fs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const dbPath = path.join(__dirname, "..", "data", "agents.db");
const SCHEMA_VERSION = 3;

// ========== 1. 幂等建表：先确保所有表存在 ==========
// 原则：启动绝不删库。所有 CREATE TABLE 用 IF NOT EXISTS。
// 未来真改 schema 时，这里加新表/新列用幂等写法。

function ensureSchema(db: Database.Database) {
  db.exec(`
    CREATE TABLE IF NOT EXISTS schema_version (
      version INTEGER PRIMARY KEY
    );

    CREATE TABLE IF NOT EXISTS agents (
      id INTEGER PRIMARY KEY,
      name TEXT NOT NULL UNIQUE,
      cn_name TEXT NOT NULL,
      priority TEXT NOT NULL,
      emoji TEXT DEFAULT '🤖',
      desc TEXT DEFAULT '',
      role TEXT DEFAULT '',
      how_to_trigger TEXT,
      process TEXT,
      boundary TEXT,
      tools TEXT,
      depends_on TEXT,
      install_status TEXT DEFAULT 'not-installed',
      install_date TEXT,
      runtime_status TEXT DEFAULT 'idle',
      last_seen TEXT,
      updated_at TEXT
    );

    CREATE TABLE IF NOT EXISTS install_log (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      agent_name TEXT NOT NULL,
      result TEXT NOT NULL,
      detail TEXT,
      created_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS status_events (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      agent_name TEXT NOT NULL,
      event_type TEXT NOT NULL,
      event_data TEXT,
      created_at TEXT NOT NULL
    );
  `);
}

// ========== 2. 列级幂等迁移：PRAGMA table_info + ALTER TABLE ADD COLUMN ==========
// 读取 agents 表现有列 → 和目标列清单对比 → 缺啥补啥
// SQLite ALTER TABLE 限制：不能加 PRIMARY KEY / UNIQUE / FOREIGN KEY
// 我们的列全是普通 TEXT，没问题

interface TargetColumn {
  name: string;
  ddl: string; // ADD COLUMN 后的完整子句（含类型和 DEFAULT）
}

// agents 表目标列清单（和 routes/agents.ts 实际使用对齐）
const AGENTS_TARGET_COLUMNS: TargetColumn[] = [
  { name: "emoji",           ddl: "emoji TEXT DEFAULT '🤖'" },
  { name: "desc",            ddl: "desc TEXT DEFAULT ''" },
  { name: "role",            ddl: "role TEXT DEFAULT ''" },
  { name: "how_to_trigger",  ddl: "how_to_trigger TEXT" },
  { name: "process",         ddl: "process TEXT" },
  { name: "boundary",        ddl: "boundary TEXT" },
  { name: "tools",           ddl: "tools TEXT" },
  { name: "depends_on",      ddl: "depends_on TEXT" },
  { name: "install_status",  ddl: "install_status TEXT DEFAULT 'not-installed'" },
  { name: "install_date",    ddl: "install_date TEXT" },
  { name: "runtime_status",  ddl: "runtime_status TEXT DEFAULT 'idle'" },
  { name: "last_seen",       ddl: "last_seen TEXT" },
  { name: "updated_at",      ddl: "updated_at TEXT" },
];

function migrateAgentsTable(db: Database.Database): string[] {
  const migrationsRun: string[] = [];

  const tableInfo = db.prepare("PRAGMA table_info(agents)").all() as { name: string }[];
  const existingCols = new Set(tableInfo.map((c) => c.name));

  for (const tc of AGENTS_TARGET_COLUMNS) {
    if (!existingCols.has(tc.name)) {
      db.exec(`ALTER TABLE agents ADD COLUMN ${tc.ddl}`);
      migrationsRun.push(`  +${tc.name}`);
    }
  }

  return migrationsRun;
}

// ========== 3. 启动：绝不删库，幂等建表 + 迁移 + upsert seed ==========

const dbExists = fs.existsSync(dbPath);
export const db = new Database(dbPath);
db.pragma("journal_mode = WAL");

ensureSchema(db);
const migrations = migrateAgentsTable(db);

// ========== 4. Seed 11 agents —— INSERT OR IGNORE（有就不动，保留已装状态） ==========
const seed = [
  { id:1, name:"drize-engineer", cn_name:"推演引擎", priority:"P0", emoji:"🧠",
    desc:"改代码/花钱/不可逆前必调",
    role:"按 1号指令铁律 #22-32 执行全链路推演。在动手前证明：动作合理、有效、现实撑得住。",
    manual:["推演一下","对比方案","正反推","值不值得做"],
    auto:["改 ≥3 行代码自动触发","花钱/上传/发布前自动触发"],
    process:["先正推照这个做法走会发生什么","再反推假设已经失败往回找死因","失败轨道：依赖点+边界点+随机","竞品五张牌：联网拉 5 个实据","出推演卡：结论+回滚点+GAP 回流"],
    boundary:["不许只推顺风局","不许编造竞品数据","不许跳推演直接开干"],
    tools:["read","search","web_search","shell"],
    depends_on:["competitor-surveyor"] },
  { id:2, name:"arch-designer", cn_name:"架构师", priority:"P0", emoji:"🏗️",
    desc:"新项目/大重构必调",
    role:"从 01 基础设施层 → 02 平台层 → 03 应用层设计整体架构。目标：模块边界清晰、依赖方向正确。",
    manual:["设计架构","技术选型","架构图"],
    auto:["新项目初始化自动触发"],
    process:["扫现有代码/README/AGENTS.md","确定技术栈对齐约束","画 01→02→03 层架构","联网查竞品架构","输出架构卡"],
    boundary:["不许选 AGENTS 禁止的技术栈","不许跳过模块边界","不许只给结论不给依据"],
    tools:["read","search","web_search","shell"],
    depends_on:["project-taker"] },
  { id:3, name:"project-taker", cn_name:"接手员", priority:"P0", emoji:"🤝",
    desc:"新接手项目必调",
    role:"快速搞懂陌生项目并产出开工包。目标：消除「接手恐惧」——下一个 AI 不用再走一遍摸索。",
    manual:["帮我接手","摸家底","扫一下"],
    auto:["刚打开新目录自动触发"],
    process:["扫目录+package.json+README","真跑一遍构建/测试","查 git log 看最近在改什么","看有没有 AGENTS.md","输出项目画像"],
    boundary:["不许瞎猜技术栈，必须真跑验证","不许漏 AGENTS.md 检查"],
    tools:["read","search","shell"],
    depends_on:["agents-builder"] },
  { id:4, name:"ledger-keeper", cn_name:"台账登记员", priority:"P0", emoji:"📒",
    desc:"开工/动手/收尾自动登记",
    role:"保证所有操作有记录、每条记录有编号、编号不重复、可随时回溯。铁律：聊天里说过没登记的 = 等于没发生过。",
    manual:["记台账","登记","编号"],
    auto:["Hook：开工/动手/收尾自动调"],
    process:["分配下一个可用台账编号","记录谁+干了啥+时间+输出在哪","写入 CHANGELOG.md 或对话卡","检查遗漏"],
    boundary:["不许编造台账编号","不许漏开工/动手/收尾三处"],
    tools:["read","search","edit"],
    depends_on:[] },
  { id:5, name:"rca-analyst", cn_name:"RCA 根因分析师", priority:"P1", emoji:"🔍",
    desc:"出 bug 后五步根因→吸收",
    role:"根因不许停在「人为错误」。每个 RCA 必须产出至少一条回归测试 RT-xx。",
    manual:["分析 bug","根因","RCA"],
    auto:["Hook：测试失败自动调"],
    process:["定位现象→复现→找根因（5 Why）","区分代码/设计/流程漏洞","吸收进 REG-xx 判落点","产出 ≥1 条 RT-xx","验证修复"],
    boundary:["不许停在「人为错误」就完了","不许边修边找根因"],
    tools:["read","search","shell"],
    depends_on:[] },
  { id:6, name:"competitor-surveyor", cn_name:"竞品调研员", priority:"P1", emoji:"🔭",
    desc:"立项前 5 竞品实据含失败案",
    role:"编造竞品数据比没有竞品更严重。联网拉 5 张实据，每张带来源链接和可验证数据。",
    manual:["调研竞品","行业分析","有人做过吗"],
    auto:["推演引擎 Step 6 自动调"],
    process:["明确调研目标","联网搜 5 个竞品含失败案例","记录谁+做了啥+成没成+来源","分析差距","输出竞品卡"],
    boundary:["不许只列 3 个必须凑 5 个","不许漏失败案例","不许编造"],
    tools:["web_search","read"],
    depends_on:[] },
  { id:7, name:"backend-expert", cn_name:"后端专家", priority:"P1", emoji:"⚙️",
    desc:"写 API/改 DB/加鉴权",
    role:"铁律：先想是不是一定要写，再选最高效的办法。不要重造轮子，能复用就复用。",
    manual:["写 API","改数据库","加鉴权","后端"],
    auto:["识别到后端任务自动调"],
    process:["读现有后端代码和 DB schema","按 AGENTS.md 技术栈来","设计接口契约","安全审查","输出接口设计"],
    boundary:["不许跳过接口契约直接写实现","不许硬编码密码/key"],
    tools:["read","search","edit","shell"],
    depends_on:[] },
  { id:8, name:"safety-auditor", cn_name:"安全巡视员", priority:"P1", emoji:"🛡️",
    desc:"commit/发版前双检",
    role:"安全与版本双检。核心铁律：版本号方向绝对不能反，安全红线绝对不能碰。",
    manual:["检查一下","安全审计","扫一下"],
    auto:["Hook：commit/push/发版前自动调"],
    process:["扫 .env / 硬编码密码","查版本号是否全对齐","检查别碰文件","跑依赖漏洞扫描","输出安全清单"],
    boundary:["不许跳过硬编码扫描","不许放过禁止文件改动"],
    tools:["read","search","shell"],
    depends_on:[] },
  { id:9, name:"report-writer", cn_name:"汇报出图员", priority:"P1", emoji:"📊",
    desc:"对话收尾/里程碑自动出卡",
    role:"保证所有汇报严格遵循 report-template。铁律：全简体中文+树结构+代码块+四件套+总结。",
    manual:["总结一下","出汇报","进度"],
    auto:["Hook：对话收尾自动调"],
    process:["汇总三层报告","更新项目进程","生成 DC-xx 对话卡","按模板输出"],
    boundary:["不许漏四件套","不许用旧格式"],
    tools:["read","edit"],
    depends_on:[] },
  { id:10, name:"agents-builder", cn_name:"AGENTS.md 构建员", priority:"P1", emoji:"🛠️",
    desc:"新项目自动建 AGENTS.md",
    role:"AGENTS 是项目设计层，只管做什么和在哪做。执行层规矩不属于 AGENTS.md。",
    manual:["帮我建 AGENTS","项目规范"],
    auto:["新项目无 AGENTS 自动调"],
    process:["扫 package.json / 目录 / README / git log","推断技术栈+项目类型","生成七块区","如有需要生成子目录嵌套","等确认后写入"],
    boundary:["不许编造技术栈","生成后必须等确认"],
    tools:["read","search","shell","edit"],
    depends_on:[] },
  { id:11, name:"mcp-finder", cn_name:"MCP 搜索员", priority:"P1", emoji:"🧩",
    desc:"联网搜 4 来源找 MCP",
    role:"铁律：找不到就说找不到，不许瞎编。官方 awesome + Trae 社区 + npm/pip + GitHub 4 来源必查。",
    manual:["有没有 MCP","找 MCP","装 MCP"],
    auto:["主 Agent 判断需要新工具时自动调"],
    process:["必查 4 来源","按 star≥20 + 近 3 月 commit 过滤","给仓库+star+安装命令+工具","找不到如实说"],
    boundary:["不许编造 MCP","不许推荐 star<5 或超 6 月没维护的"],
    tools:["web_search","read"],
    depends_on:[] },
];

// 用 INSERT OR IGNORE：已存在的 agent 完全不动（install_status / install_date 保留！）
// 只有全新 DB（或缺行）才插入新 agent
const insertAgentUpsert = db.prepare(`
INSERT OR IGNORE INTO agents (id, name, cn_name, priority, emoji, desc, role, how_to_trigger, process, boundary, tools, depends_on, updated_at)
VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'))
`);

let inserted = 0;
let skipped = 0;
for (const a of seed) {
  const info = insertAgentUpsert.run(
    a.id, a.name, a.cn_name, a.priority, a.emoji,
    a.desc, a.role,
    JSON.stringify({ manual: a.manual, auto: a.auto }),
    JSON.stringify(a.process),
    JSON.stringify(a.boundary),
    JSON.stringify(a.tools),
    JSON.stringify(a.depends_on)
  );
  if (info.changes > 0) inserted++; else skipped++;
}

// 版本表：永远存当前版本，但不触发删库
db.prepare(`INSERT OR REPLACE INTO schema_version (version) VALUES (${SCHEMA_VERSION})`).run();

// ========== 5. 启动日志 ==========
console.log(`[db] SQLite v${SCHEMA_VERSION} ready at ${dbPath}`);
if (!dbExists) console.log(`[db]  新库：首次初始化`);
else console.log(`[db]  旧库：已存在，绝不删库 ✅`);
if (migrations.length > 0) {
  console.log(`[db]  列级迁移：`);
  for (const m of migrations) console.log(m);
} else {
  console.log(`[db]  列级迁移：无（agents 表列已齐）`);
}
console.log(`[db]  seed：插入 ${inserted} 行，跳过 ${skipped} 行（已存在不动）`);

// ========== 6. Helper queries ==========
export const queries = {
  getAll: db.prepare("SELECT * FROM agents ORDER BY id"),
  getByName: db.prepare("SELECT * FROM agents WHERE name = ?"),
  updateInstall: db.prepare(`
    UPDATE agents SET install_status = ?, install_date = ?, updated_at = datetime('now')
    WHERE name = ?
  `),
  updateRuntime: db.prepare(`
    UPDATE agents SET runtime_status = ?, last_seen = datetime('now'), updated_at = datetime('now')
    WHERE name = ?
  `),
  insertEvent: db.prepare(`
    INSERT INTO status_events (agent_name, event_type, event_data, created_at)
    VALUES (?, ?, ?, datetime('now'))
  `),
  recentEvents: db.prepare("SELECT * FROM status_events ORDER BY id DESC LIMIT 100"),
};

// JSON deserializer helper used by routes
export function parseAgentRow(row: any) {
  return {
    id: row.id,
    name: row.name,
    cn_name: row.cn_name,
    priority: row.priority,
    emoji: row.emoji,
    desc: row.desc,
    role: row.role,
    how_to_trigger: row.how_to_trigger ? JSON.parse(row.how_to_trigger) : { manual: [], auto: [] },
    process: row.process ? JSON.parse(row.process) : [],
    boundary: row.boundary ? JSON.parse(row.boundary) : [],
    tools: row.tools ? JSON.parse(row.tools) : [],
    depends_on: row.depends_on ? JSON.parse(row.depends_on) : [],
    install_status: row.install_status,
    install_date: row.install_date,
    runtime_status: row.runtime_status,
    last_seen: row.last_seen,
    updated_at: row.updated_at,
  };
}
