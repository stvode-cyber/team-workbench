---
project: AI 专岗员工可视化工作台
version: "3.0"
updated: 2026-10-31
owner: 1号指令母版配套
stage: "v3.0 MVP 全绿交付"
---

# AI 专岗员工可视化工作台 · AGENTS.md

> 本文件是项目**设计层**（业务方向 / 技术栈 / 目录约定 / 安全红线）。
> 执行层规矩（铁律 71 条 / 四件套格式 / 树结构输出）在 1号指令 v6.4-6 里，不在此文件。

---

## ① 项目索引盘

| 项 | 值 |
|---|---|
| 项目名 | AI 专岗员工可视化工作台（专岗工作台 v2.0） |
| 一句话 | 给 AI 团队造的"员工面板"——11 个专岗 Subagent，各有状态灯、调动指引、完整职责说明 |
| 当前阶段 | `[>]` v3.0 MVP 全栈开发中（后端 Node/Express/SQLite + 前端 Vue3 + WebSocket） |
| 核心产出 | 1 个 HTML 可视化面板 + 11 套 Subagent 三件套（agent.md + SKILL.md + README.md）+ 1 套母文件打包 |
| 目标用户 | 1号指令体系使用者 / Trae AI 团队 |
| 生成日期 | 2026-10-30 |

---

## ② 技术栈精确版号

| 类 | 技术 | 版本 | 备注 |
|---|---|---|---|
| 后端运行时 | Node.js | ≥18 | ES Modules + TypeScript（v3.0 新增） |
| 后端框架 | Express | ≥4.18 | REST API 路由（v3.0 新增） |
| 实时通信 | ws | ≥8.16 | WebSocket 推送状态变更（v3.0 新增） |
| 数据库 | better-sqlite3 | ≥11 | 零配置 SQLite，自托管（v3.0 新增） |
| 前端框架 | Vue | 3.x | Composition API + Vite 构建（v3.0 新增） |
| 前端终端 | xterm.js | ≥5.3 | 面板内嵌终端（v3.0 新增） |
| 调用链画布 | d3.js / react-flow 替代 |待定 | AutoGen Studio 风格拖拽画布（v3.0 Phase 2） |
| 现有面板 | HTML5 + 原生 JS | - | 已交付 v2.0，作为原型参考（后续迁移到 Vue3） |
| 文档 | Markdown | CommonMark | 所有 agent/Skill/README 均为 .md |
| AI 平台 | Trae | 需 1号指令 v6.4-6 + 内置 Skill 体系 | agent.md 复制到 `.trae/agents/` 即生效 |

**v3.0 特点：后端 + WebSocket 真实时 + Vue3 全栈。** 后端负责 SQLite 存状态、Hook 推 JSON 进 DB、WebSocket 推给前端；前端从纯静态迁移到 Vue3 SPA。现有 650 行静态面板作为原型参考，不废弃。

---

## ③ 文件结构地图

```
团队工作台/                                     ← 项目根（设计层）
├── AGENTS.md                                    ← 本文件（项目设计层·总纲）
├── 项目进程.md                                  ← 项目进度追踪（固定中文名）
├── AI专岗员工面板.html                           ← 可视化面板（根目录独立副本）
├── AI加强体系说明.md                             ← 5 层加强体系大白话（根目录独立副本）
│
└── 专岗工作台/                                   ← 主体目录
    ├── 00_母文件/                                ← 总入口文档（人读）
    │   ├── README.md                             ← 目录说明 + 安装指引
    │   └── AI加强体系说明.md                     ← 5 层加强体系完整说明
    │
    ├── 01_总览面板/                              ← 可视化面板（可独立部署）
    │   └── 员工面板.html                         ← 和根目录那份内容相同
    │
    ├── 02_AGENTS/                                ← 11 个专岗（每个独立目录·三件套）
    │   ├── 01_drize-engineer/                    ← 推演引擎 · P0
    │   │   ├── agent.md                          ← Subagent 定义（YAML frontmatter + prompt）
    │   │   ├── SKILL.md                          ← 专属知识（推演 SOP + 铁律 #22-32）
    │   │   └── README.md                         ← 使用说明（人读）
    │   ├── 02_arch-designer/                     ← 架构师 · P0
    │   │   └── agent.md + SKILL.md + README.md
    │   ├── 03_project-taker/                     ← 接手员 · P0
    │   │   └── agent.md + SKILL.md + README.md
    │   ├── 04_ledger-keeper/                     ← 台账登记员 · P0
    │   │   └── agent.md + SKILL.md + README.md
    │   ├── 05_rca-analyst/                       ← RCA 根因分析师 · P1
    │   │   └── agent.md + SKILL.md + README.md
    │   ├── 06_competitor-surveyor/               ← 竞品调研员 · P1
    │   │   └── agent.md + SKILL.md + README.md
    │   ├── 07_backend-expert/                    ← 后端专家 · P1
    │   │   └── agent.md + SKILL.md + README.md
    │   ├── 08_safety-auditor/                    ← 安全巡视员 · P1
    │   │   └── agent.md + SKILL.md + README.md
    │   ├── 09_report-writer/                     ← 汇报出图员 · P1
    │   │   └── agent.md + SKILL.md + README.md
    │   ├── 10_agents-builder/                    ← AGENTS.md 构建员 · P1
    │   │   └── agent.md + SKILL.md + README.md
    │   └── 11_mcp-finder/                        ← MCP 搜索员 · P1
    │       └── agent.md + SKILL.md + README.md
    │
    ├── 03_开源生态/                              ← 精选清单（参考用）
    │   ├── Skill-精选.md                         ← 第三方 Skill 推荐
    │   ├── MCP-精选.md                           ← 第三方 MCP 推荐
    │   └── Hook-模板.md                          ← Hook 配置模板
    │
    └── 04_母文件打包/                            ← 可直接分发的母版 .md
        ├── 推演引擎.md                           ← 推演引擎母版
        ├── 架构师.md                             ← 架构师母版
        ├── ...（共 11 份，对应 11 个专岗）
        └── 项目负责人-模板.md                    ← 扩展模板
```

---

## ④ 功能设计指针

### 面板功能（`AI专岗员工面板.html`）
- **左侧栏**：按优先级分组显示 11 个专岗，每个有 emoji 头像 + 中文名 + 英文名标签 + 一句话描述 + 状态灯（🟢 待命 / 🟡 忙碌）
- **右侧详情**：点击左侧任意专岗后显示
  - 头像 + 中文名 + 英文名 + 优先级标签
  - 「一句话说明」section
  - 「什么时候调我」section（触发条件列表）
  - 「工作流程」section（步骤列表）
  - 「硬性边界（绝对不许）」section
  - 「拥有工具」section
  - 「使用方式」section + 安装位置提示
- **数据结构**：JS 里 `const agents = [{emoji, en, cn, priority, desc, when, process, boundary, tools, usage}]` 硬编码
- **版本说明**：header 写死 `v6.4-6 · 11 个专岗 Subagent · 全部就绪待命`

### Agent 三件套边界（严格分开）
| 文件 | 内容 | AI 自动读？ | 人读？ |
|---|---|---|---|
| `agent.md` | 角色定义（谁、干什么、何时被调、硬性边界） | 是（Trae 内置 Subagent） | 是 |
| `SKILL.md` | 专属知识（SOP、铁律原文、检查清单） | 按需加载 | 是 |
| `README.md` | 使用说明（怎么装、怎么触发、输出长啥样） | 否 | 是 |

**禁止**：agent.md 里塞知识细节（污染主对话），SKILL.md 里塞角色定义（混淆 Skill 定位）。

### 11 个专岗职责速查
| # | 目录名 | 中文名 | 优先级 | 核心能力 |
|---|---|---|---|---|
| 01 | drize-engineer | 推演引擎 | 🔴 P0 | 改 ≥3 行/花钱/不可逆前必调，正推+反推+失败轨道+5 竞品实据 |
| 02 | arch-designer | 架构师 | 🔴 P0 | 新项目/大重构，01-03 层架构+模块边界+技术选型 |
| 03 | project-taker | 接手员 | 🔴 P0 | 扫家底+技术栈+补 AGENTS.md+产出开工包 |
| 04 | ledger-keeper | 台账登记员 | 🔴 P0 | 开工/动手/收尾三处自动登记 REG/IMP/DC |
| 05 | rca-analyst | RCA 根因分析师 | 🟡 P1 | bug/故障/返工后，五步根因→吸收→产 RT-xx |
| 06 | competitor-surveyor | 竞品调研员 | 🟡 P1 | 立项前 5 竞品实据（含 1 失败案例） |
| 07 | backend-expert | 后端专家 | 🟡 P1 | API/DB/Auth/Observability 04-06 层检查 |
| 08 | safety-auditor | 安全巡视员 | 🟡 P1 | commit/发版前版本对齐+硬编码扫描+别碰文件 |
| 09 | report-writer | 汇报出图员 | 🟡 P1 | 对话收尾/里程碑，三层报告+四件套 |
| 10 | agents-builder | AGENTS.md 构建员 | 🟡 P1 | 新项目自动侦查→生成 AGENTS.md |
| 11 | mcp-finder | MCP 搜索员 | 🟡 P1 | 联网搜 4 来源，找不到就说找不到 |

### 新增员工管制协议（**强制门槛 · 不过不许写 agent.md**）

> 新增专岗 Agent = 在 `02_AGENTS/` 下建新目录 + 三件套 + 同步面板。
> **任何一条门槛没通过 → 不许写 agent.md → 停止流程 → 登记 GAP-xx。**
> 违反后果：重名导致 Subagent 注册冲突 / 能力重叠导致主 Agent 调谁都困惑 / 面板和安装脚本漏掉新员工。

**前置检查 1：硬冲突——英文名（agent name）必须唯一**
- ✅ 允许：`vibe-dash-agent`、`feature-flags-engineer`
- ❌ 禁止：和已有 11 个重名（如再建一个 `rca-analyst`）
- 验证方式：`Get-ChildItem 02_AGENTS/*/agent.md | Select-String "^name:"` 全量扫一遍

**前置检查 2：软冲突——能力必须"补位"，不能"抢活"**
- 每个已有 Agent 有明确的核心能力边界（见上表"核心能力"列）
- 新员工的核心能力**不能同时覆盖任何已有 Agent 已覆盖的事情**
  - ❌ 冲突：建一个 "bug-fixer"——rca-analyst 已经管 bug 根因分析+修复
  - ❌ 冲突：建一个 "deployment-expert"——safety-auditor 已经管发版前双检
  - ✅ 补位：建一个 "release-automator"——safety-auditor 管发版前扫描，新员工管发版后自动部署+监控告警
- 验证方式：对照上表逐一比对核心能力描述，写出差距对比（不是重名 ≠ 没冲突）

**前置检查 3：必须有 Skill 知识支撑**
- agent.md 只写角色定义（谁 + 干什么 + 何时调 + 边界）
- 必须同时写 SKILL.md（SOP / 检查清单 / 铁律原文支撑）
- ❌ 禁止：只有 agent.md 没有 SKILL.md（角色变成空壳，AI 不知怎么干活）

**前置检查 4：安装链路必须同步**
- 新员工加完目录后，以下 4 处**必须同步更新**，少一处 = 安装链路断：
  1. 面板 JS 里的 `const agents = [...]` 数组（双份面板都要改）
  2. `scan-install-status.ps1` 里的 `$agentNames` 数组
  3. `install-all-agents.ps1` 里的 `$agentNames` 数组
  4. AGENTS.md 本文件的「11 个专岗职责速查」表 → 更新为 12 个并重新编号
- 验证方式：`scan-install-status.ps1` 真跑一遍，新员工出现在 JSON 里

**前置检查 5：优先级和编号必须对齐**
- 已有编号是 01-11，新员工**不允许插号**——必须追加到末尾（12、13...）
- 优先级（P0/P1）必须和核心能力匹配：
  - P0 核心岗：改 ≥3 行代码前 / 花钱前 / 不可逆前必调的
  - P1 支援岗：可选、不阻塞主流程的

**流程总结（不可绕过）：**
```
[1] 写出新员工提案：中文名 + 英文名 + 核心能力 + 优先级
    ├─ 过检查 1（硬冲突 · 重名？）
    ├─ 过检查 2（软冲突 · 抢活？）
    └─ 过检查 5（优先级对不对？编号追加？）
        ↓ 全过
[2] 写 agent.md（角色定义，不塞知识细节）
    写 SKILL.md（SOP + 检查清单）
    写 README.md（使用说明）
    └─ 过检查 3（Skill 有吗？）
        ↓
[3] 改面板 JS（加新员工到 agents 数组，双份同步）
    改 install 脚本（$agentNames 追加）
    改 AGENTS.md 速查表（更新编号和数量）
    └─ 过检查 4（安装链路同步了？）
        ↓
[4] 真跑 scan-install-status.ps1 → 新员工出现在 JSON 里
    ✅ 通过 → 登记 REG-ADD-xx 进变更日志
    ❌ 没过 → 回 [3] 改
```

---

## ⑤ 可执行命令清单

本项目零构建脚本。以下是**安装和使用**相关命令：

```powershell
# === 安装单个专岗（以推演引擎为例） ===

# Step 1: 复制 agent.md 到 Trae 全局 agents 目录
Copy-Item .\专岗工作台\02_AGENTS\01_drize-engineer\agent.md `
  "$env:USERPROFILE\.trae-cn\agents\drize-engineer.md"

# Step 2: 复制 SKILL.md 到 Trae 全局 skills 目录
New-Item -ItemType Directory "$env:USERPROFILE\.trae-cn\skills\drize-engineer" -Force
Copy-Item .\专岗工作台\02_AGENTS\01_drize-engineer\SKILL.md `
  "$env:USERPROFILE\.trae-cn\skills\drize-engineer\SKILL.md"

# Step 3: 重启 Trae 或打开新对话，AI 自动识别

# === 一键安装全部 11 个专岗 ===
# （如果需要，后续可以写一个 .ps1 批量脚本）

# === 本地预览面板 ===
# 直接双击打开：
#   .\AI专岗员工面板.html
# 或用 PowerShell 打开：
Start-Process ".\AI专岗员工面板.html"
```

---

## ⑥ 三色边界灯

### ✅ 允许做的
- 编辑 `AI专岗员工面板.html` 的 agents 数据（加字段、改描述、改状态逻辑）
- 编辑 11 个 `agent.md` 的角色定义和硬性边界（只增量改，不许重写）
- 编辑 11 个 `SKILL.md` 的知识内容
- 编辑 11 个 `README.md` 的人读说明
- **新增专岗（需过"新增员工管制协议"5 条强制门槛）**
- 同步更新根目录和 `01_总览面板/` 的面板 HTML 副本
- 更新 `04_母文件打包/` 里的母版内容

### 🚫 绝对禁止的
- 在任何 `.md` / `.html` 里硬编码敏感数据（密码 / token / 密钥 / 私钥）
- 删除 `00_母文件/` 里的版本说明（那是母版配套文档）
- 把执行层规矩（1号指令铁律）塞进 `AGENTS.md` 或 `agent.md`（设计/执行层必须分开）
- 整体重写已有 `agent.md`（只允许增量改，保留已有内容）
- 编造 agent.md 里的"硬性边界"和"工作流程"（必须真有对应的 Skill / 铁律原文支撑）
- **跳过"新增员工管制协议"的 5 条门槛直接写 agent.md**（软冲突/硬冲突/Skill 缺失/安装链路不同步 → 必须停）
- **在 `02_AGENTS/` 下插新编号（如 11.5）**（必须追加到末尾 12、13...）

---

## ⑦ 变更追踪日志

| 日期 | 编号 | 改了啥 | 原因 | 影响范围 |
|---|---|---|---|---|
| 2026-10-31 | REG-MVP-DELIVER-001 ✅ | **v3.0 MVP 全绿交付**：后端 Node/Express/SQLite/ws 11 路由 + 前端 Vue3 12 源文件 + 管制协议后端硬拦 + WebSocket 3 事件 + 浏览器 5/5 PASS | 从 v2.0 原型升级到全栈 MVP | server/ + web/ + scripts/ |
| 2026-10-31 | REG-DB-PERSIST-001 ✅ | db.ts C 方案完整迁移层（幂等建表 + seed upsert + PRAGMA 列级补列） | 每次重启 DB 归零（设计缺陷） | server/src/db.ts |
| 2026-10-31 | REG-VUE-TSC-001 ✅ | vue-tsc 5 处类型错误修复（Booleanish + emit 签名 + runtime_status） | Vue 3.5 Booleanish 更严格 | web/src/App.vue + AgentDetail.vue |
| 2026-10-31 | REG-INSTALL-001 | 新增员工管制协议（5 条强制门槛 + 不可绕过流程） | 用户需求「加员工要管制别冲突」 | ④ 功能设计指针（新增 2 板块） |
| 2026-10-31 | REG-PANEL-002 | 面板加安装状态灯 + 调用链图 + 一键复制安装命令 | 上一回合功能增强 | `AI专岗员工面板.html` + 双份同步 + 3 个 PowerShell 脚本 |
| 2026-10-31 | REG-AGENTS-INIT | 新建 `AGENTS.md`（七块区完整版） | 项目刚交付还没设计层文档 | 根目录 · 全项目设计层 |
| 2026-10-31 | REG-AGENTS-INIT | 新建 `项目进程.md`（里程碑追踪） | 接手铁律 #19 要求第一时间建 | 根目录 |
| 2026-10-31 | REG-PANEL-001 | 面板增强：状态动态轮播 + 调动指引 section + 员工说明 section | 用户需求「展示员工状态和调动指引」 | `AI专岗员工面板.html` + `01_总览面板/员工面板.html` |
| 2026-10-30 | （原始交付） | 交付 v2.0：00-04 四大分区 + 11 专岗三件套 + 2 份 HTML 面板 | 1号指令母版配套初始化 | 全项目 |

---

## ⑧ 前端定案（v3.0 · 2026-10-31 · 5 项决策已冻结）

### 竞品调研整合（之前联网查的 5 张牌核心发现）

| 竞品 | 核心能力 | 后端 | 重？ | 我们独特点 |
|---|---|---|---|---|
| AgentLens（MIT 开源） | Agent 执行记录仪（tool call / cost / trace） | SQLite + API | 中 | 他们做"跑了啥"，我们做"是谁" |
| Vibe Dash（Apache2） | Kanban + Burndown + MCP 原生 | Node + SQLite + WS | 中 | 同上 + 我们中文原生 |
| AutoGen Studio 2.0（Microsoft） | 拖拽画布多 Agent 编排 | 完整后端 | 重 | 他们做"拼工作流"，我们做"入职手册" |
| Coze / Dify（字节） | 低代码 Bot 构建（左编排中对话右配置） | SaaS / 自托管 | 重 | 面向外部用户，不是团队内部 |
| SuperAGI（已停更） | 单一公司驱动，公司变方向项目就死 | - | - | **反面教材**——不能只靠一个人维护 |

**核心取舍：** 竞品全做"执行面板"，没人做"员工入职介绍卡"。v2.0 纯静态 HTML 已验证"介绍卡"视角成立；v3.0 加后端让它从"死文档"变成"活管理系统"。

### 范围冻结（用户 2026-10-31 直接选，不再复议）

| 编号 | 决策项 | 最终选择 |
|---|---|---|
| D1 | 技术栈 | **B. Node/Express + SQLite**（后端 + WebSocket + Vue3 SPA） |
| D2 | 调用链可视化 | **B. AutoGen 风格拖拽画布**（v3.0 Phase 2） |
| D3 | 状态实时性 | **B. WebSocket 真实时**（后端推，前端不轮询） |
| D4 | 安装功能 | **B. 面板内嵌终端 xterm.js**（v3.0 Phase 3） |
| D5 | 新增功能 | **A+B. 搜索框 + 状态筛选**（MVP 必做） |

### 三阶段开发路线图

```
v3.0 MVP（先做这个，最小可交付闭环）
├─ 后端骨架：Node + Express + better-sqlite3 + ws
│   ├─ SQLite schema：agents 表 + install_log 表 + status_events 表
│   ├─ REST API：GET /agents · GET /agents/:id · POST /agents/:id/scan
│   ├─ WebSocket：ws://host:port/events（状态变更推送）
│   └─ Hook 改：scan-install-status.ps1 → 写 DB（不再写 JSON）
├─ 前端骨架：Vue3 + Vite
│   ├─ 现有 650 行 HTML 面板 → 迁移成 Vue SFC
│   ├─ 搜索框（顶栏，搜 agent 名 + 触发词）
│   ├─ 状态筛选（P0/P1 · 已装/未装 · 忙碌/待命）
│   └─ WebSocket 订阅（状态变更实时更新面板，不轮询）
└─ 交付标准：后端启动 → 前端跑 → Hook 推状态 → 面板实时变 → 搜索筛选能用

v3.0 Phase 2（MVP 跑通后加）
├─ 调用链拖拽画布（AutoGen Studio 风格）
├─ 能改 agent 之间的 dependsOn 关系
├─ 保存后自动写回 agent.md + 面板 JS
└─ 交付标准：画布能拖 → 连接关系能改 → 保存能生效

v3.0 Phase 3（最后加）
├─ xterm.js 内嵌终端
├─ 面板里点"安装"按钮 → 终端里跑 install-single-agent.ps1
├─ 实时显示安装日志
└─ 交付标准：面板里点安装 → 终端输出 → 装完状态灯变绿
```

### MVP 文件结构（开发前冻结，不再复议）

```
团队工作台/
├── server/                      ← v3.0 新增（后端）
│   ├── package.json
│   ├── tsconfig.json
│   ├── src/
│   │   ├── index.ts             ← Express + ws 启动入口
│   │   ├── db.ts                ← SQLite 连接 + schema
│   │   ├── routes/
│   │   │   └── agents.ts        ← REST API
│   │   └── ws.ts                ← WebSocket 推送
│   └── data/
│       └── agents.db            ← SQLite 数据库文件（自动生成）
├── web/                         ← v3.0 新增（前端 Vue3）
│   ├── package.json
│   ├── vite.config.ts
│   ├── src/
│   │   ├── main.ts
│   │   ├── App.vue
│   │   ├── components/
│   │   │   ├── AgentSidebar.vue ← 左侧花名册（从 HTML 面板迁移）
│   │   │   ├── AgentDetail.vue  ← 右侧详情
│   │   │   └── SearchBar.vue    ← 顶栏搜索 + 筛选
│   │   └── api/
│   │       ├── agents.ts        ← REST 调用
│   │       └── ws.ts            ← WebSocket 客户端
│   └── index.html
├── scripts/
│   ├── scan-install-status.ps1  ← 改：从写 JSON 改成写 SQLite
│   ├── install-single-agent.ps1 ← 不变
│   ├── install-all-agents.ps1  ← 不变
│   └── pre-check-new-agent.ps1  ← 不变
├── AGENTS.md                    ← 设计层（本文件）
├── AI专岗员工面板.html           ← v2.0 原型保留，不废弃
└── 专岗工作台/                   ← 原始交付物，不动
```

### 数据库 Schema（MVP 冻结）

```sql
-- agents 表：11 个专岗的当前状态
CREATE TABLE agents (
  id INTEGER PRIMARY KEY,           -- 01-11
  name TEXT NOT NULL UNIQUE,        -- 英文名（drize-engineer）
  cn_name TEXT NOT NULL,            -- 中文名（推演引擎）
  priority TEXT NOT NULL,           -- P0 / P1
  install_status TEXT,              -- installed / partial / not-installed
  install_date TEXT,                -- YYYY-MM-DD
  runtime_status TEXT,              -- idle / busy
  last_seen TEXT,                   -- ISO timestamp
  depends_on TEXT,                  -- JSON array ["competitor-surveyor"]
  updated_at TEXT                   -- ISO timestamp
);

-- install_log：每次安装记录
CREATE TABLE install_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  agent_name TEXT NOT NULL,
  result TEXT NOT NULL,             -- success / failed
  detail TEXT,                      -- 错误信息
  created_at TEXT NOT NULL
);

-- status_events：WebSocket 推送用（环形缓冲，保留最近 1000 条）
CREATE TABLE status_events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  agent_name TEXT NOT NULL,
  event_type TEXT NOT NULL,         -- install / scan / runtime
  event_data TEXT,                  -- JSON
  created_at TEXT NOT NULL
);
```

### 开发顺序（最小可交付闭环，先跑通骨架再长肉）

```
Step 1: server/ 骨架（package.json + Express + SQLite + ws 启动）
        → 跑通 npm install + npm run dev + ws 连接成功
Step 2: SQLite schema + seed（11 个 agent 初始数据）
        → GET /agents 返回 11 个
Step 3: scripts/scan-install-status.ps1 改写 DB
        → 跑 scan → SQLite 里 install_status 变
Step 4: ws.ts 推送 + 前端 Vue3 骨架（Vite）
        → SQLite 变 → ws 推 → 前端面板变
Step 5: 搜索 + 筛选 + 从 HTML 面板迁移 Vue SFC
        → MVP 闭环 ✅
```
