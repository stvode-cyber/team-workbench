# MVP 交付清单 · AI 专岗员工可视化工作台 v3.0

> 交付日期：2026-10-31 · git commit：`de3913a`
> 接手第一步：先读 `HANDOFF.md`（5 分钟接棒），再读 `AGENTS.md`（3 分钟对齐设计层）

---

## 一、一句话项目

给 AI 团队造的"员工面板"——11 个专岗 Subagent，后端 REST + WebSocket 真实时 + Vue3 SPA。v3.0 MVP 全绿交付。

---

## 二、快速启动（两条命令）

```powershell
# 后端（端口 3001）
cd server; npx tsx src/index.ts
# → http://localhost:3001/health

# 前端（端口 5173）
cd web; npx vite
# → http://localhost:5173
```

> 如果 5173 被旧进程占用，先 `taskkill /PID <PID> /F` 杀掉，Vite 就会回到 5173。

---

## 三、后端 API 清单（11 路由）

| Method | Path | 说明 |
|---|---|---|
| GET | `/health` | 健康检查 |
| GET | `/api/agents` | 11 agent 列表 + summary（installed/missing/busy） |
| GET | `/api/agents/:name` | 单个 agent 详情 |
| POST | `/api/agents/scan-all` | 全扫 11 个安装状态 + 更新 DB + 广播 WebSocket |
| POST | `/api/agents/install-all` | 一键全装 11 个 |
| POST | `/api/agents/:name/install` | 单个安装 |
| POST | `/api/agents/:name/uninstall` | 单个卸载 |
| POST | `/api/agents/:name/scan` | 更新单个安装状态 |
| POST | `/api/agents/:name/status` | 更新运行状态 idle/busy |
| POST | `/api/agents` | **新增员工**（带管制协议 5 条门槛，FAIL → 422） |
| PUT | `/api/agents/:name` | 编辑（DB + 同步写 agent.md） |
| DELETE | `/api/agents/:name` | 删除（DB + 源目录 + 安装文件） |

### WebSocket

- **端点**：`ws://localhost:3001/events`
- **事件类型**：`install` / `runtime` / `scan`
- **前端 Vite 代理已配置**：`/events` → `ws://localhost:3001`

---

## 四、管制协议（新增员工必须过 5 条门槛）

后端 `POST /api/agents` 硬拦（HTTP 422），**不可绕过**：

| 门槛 | 检测方式 | FAIL 时 |
|---|---|---|
| 1. 英文名唯一 | DB + 源目录真查 | `"英文名已存在"` |
| 2. 能力补位 | 共享工具 ≥ 2 且 角色关键词撞 ≥ 2 | `"与 xxx 职责重叠"` |
| 3. 完整 Skill | role ≥ 10 字 + trigger/boundary 非空 | `"Skill 支撑不足"` |
| 4. 优先级 enum | 只能 P0 或 P1 | `"优先级非法"` |
| 5. kebab-case | 正则 `^[a-z0-9]+(-[a-z0-9]+)*$` | `"英文名格式错误"` |

前端 `AgentForm.vue` 把 5 条门槛可视化成红/绿色条，用户不用猜哪条挂了。

---

## 五、数据库（SQLite · 已持久化）

- **位置**：`server/data/agents.db`
- **Schema**：3 张表（`agents` / `install_log` / `status_events`）
- **种子数据**：11 个 agent 初始行（`id` 01~11，`install_status` 已装）
- **持久化**：启动绝不删库（C 方案完整迁移层，重启 3 次验证 installed=11 保持）

---

## 六、前端（Vue3 · 已过 vue-tsc）

- **框架**：Vue 3.5 + Vite 5.4 + TypeScript 5.6
- **源文件**：12 个（9 业务文件 + 3 配置）
- **类型检查**：`npx vue-tsc --noEmit` → 退出码 0（0 错误）
- **组件**：

| 组件 | 功能 |
|---|---|
| `App.vue` | 主布局（三栏：顶栏 + Sidebar + Detail）+ CRUD 流程控制 |
| `AgentSidebar.vue` | 左侧花名册（emoji + 中文名 + 状态灯 🟢🟡） |
| `AgentDetail.vue` | 右侧详情（8 section：说明/触发/流程/边界/工具/调用链 + 🔧管理按钮区） |
| `AgentForm.vue` | 新增/编辑表单（管制协议 5 条可视化 PASS/FAIL） |
| `SearchBar.vue` | 搜索 + 筛选（P0/P1 + installed + keyword） |

---

## 七、11 个专岗

| # | 目录名 | 中文名 | 优先级 | 核心能力 |
|---|---|---|---|---|
| 01 | drize-engineer | 推演引擎 | 🔴 P0 | 改 ≥3 行/花钱/不可逆前必调 |
| 02 | arch-designer | 架构师 | 🔴 P0 | 新项目/大重构，01-03 层架构 |
| 03 | project-taker | 接手员 | 🔴 P0 | 扫家底 + 补 AGENTS.md |
| 04 | ledger-keeper | 台账登记员 | 🔴 P0 | 开工/动手/收尾三处自动登记 |
| 05 | rca-analyst | RCA 根因分析师 | 🟡 P1 | bug/故障五步根因 |
| 06 | competitor-surveyor | 竞品调研员 | 🟡 P1 | 立项前 5 竞品实据 |
| 07 | backend-expert | 后端专家 | 🟡 P1 | API/DB/Auth/Observability |
| 08 | safety-auditor | 安全巡视员 | 🟡 P1 | commit/发版前扫描 |
| 09 | report-writer | 汇报出图员 | 🟡 P1 | 对话收尾三层报告 |
| 10 | agents-builder | AGENTS.md 构建员 | 🟡 P1 | 新项目自动侦查 → 生成 AGENTS.md |
| 11 | mcp-finder | MCP 搜索员 | 🟡 P1 | 联网搜 4 来源 MCP |

每个专岗在 `专岗工作台/02_AGENTS/<编号_目录名>/` 下有三件套：
- `agent.md` — 角色定义（Trae 自动识别）
- `SKILL.md` — 专属知识（SOP + 铁律原文）
- `README.md` — 人读说明

---

## 八、验证步骤（3 步确认 MVP 完好）

```powershell
# Step 1：后端健康 + DB 状态
Invoke-RestMethod http://localhost:3001/api/agents
# → 看 summary.total = 11, installed = 11

# Step 2：浏览器打开面板，看三件事
# http://localhost:5173
# → 11 个 emoji 头像 + 中文名（Sidebar）
# → 点击推演引擎 → 右侧显示 8 个 section
# → 点击全扫状态 → 成功弹窗 ok:11 fail:0

# Step 3：新增一个新员工，看管制协议是否生效
$body = '{"name":"test-agent","cn_name":"测试员","priority":"P1","role":"x"*20,"how_to_trigger":{"manual":["m"],"auto":["a"]},"boundary":["b"]}'
Invoke-RestMethod -Uri http://localhost:3001/api/agents -Method POST `
  -ContentType "application/json" -Body $body
# → 201 + gates: [{ gate: 1, pass: true, ... }] 5 条全绿
```

---

## 九、已知小尾巴（不阻塞 MVP）

| # | 问题 | 影响 | 修复难度 |
|---|---|---|---|
| 1 | `团队/` 目录 vs `专岗工作台/02_AGENTS` 重复 | 两个并行目录 | 小（删一套即可） |
| 2 | 批量操作文案"复制 undefined 个文件" | UI 显示 | 小（取 `copied ?? install_status`） |
| 3 | 顶栏缺 WebSocket 状态指示灯 | 功能感知 | 中（加 3 态状态灯） |
| 4 | Sidebar 缺编辑/删除按钮 | 操作对称 | 小（加 2 个 emit） |

---

## 十、后续阶段

| 阶段 | 内容 | 难度 | 状态 |
|---|---|---|---|
| **MVP ✅** | 后端 11 API + DB 持久化 + ws + 前端 Vue3 + 管制协议 | — | 已交付 `de3913a` |
| Phase 2 | 调用链拖拽画布（AutoGen 风格） | 大（react-flow 集成） | 未开始 |
| Phase 3 | xterm.js 内嵌终端 | 中 | 未开始 |

---

## 十一、核心文档入口

| 文档 | 用途 | 关键内容 |
|---|---|---|
| `HANDOFF.md` | 新 AI 接手第 1 步 | 目录地图 + 启动命令 + 踩坑教训 + 待办 |
| `AGENTS.md` | 项目设计层 | 技术栈冻结 + 管制协议 + 三色边界灯 |
| `项目进程.md` | 里程碑追踪 | 6 个里程碑 + 9 个模块状态 + 7 条跟进待办 |
| `MVP-交付清单.md` | 本文件 | 完整交付清单 + 快速启动 + 验证步骤 |
