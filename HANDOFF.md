# DC-001 · 交接台账 · 2026-10-31

> 新 AI 接手第一步：**先跑 `Get-Content HANDOFF.md`**，读完再动手。
> 铁律 #41：好的 handoff 让接手者 30 秒接棒，不用重走一遍摸索。

---

## 一、项目是什么

**AI 专岗员工面板 v3.0** — 给 AI 开发团队用的可视化员工管理工作台。

核心价值：
- 11 个专岗 Agent（推演引擎/架构师/接手员/台账/RCA/竞品/后端/安全/汇报/AGENTS 构建/MCP 搜索）
- 每个 Agent 有完整入职手册（角色/触发词/工作流程/边界/工具/调用链）
- 面板展示 + 实时状态 + 安装管理 + CRUD（带管制协议）

---

## 二、当前真实状态（2026-10-31 快照）

| 项 | 状态 | 说明 |
|---|---|---|
| 后端 Node/Express/SQLite | ✅ UP | `localhost:3001` · `npx tsx src/index.ts` |
| 前端 Vue3/Vite | ✅ UP | `localhost:5173` · npm run dev |
| WebSocket /events | ✅ 就绪 | 广播 scan/runtime/install 三类事件 |
| SQLite DB | ✅ 11 agent seed | server/data/agents.db |
| 源目录 02_AGENTS | ✅ 11 个 | 专岗工作台/02_AGENTS/01~11/ |
| 安装状态 | ✅ installed=11 | db.ts 持久化修复（C 方案）：重启不丢 ✅ |
| 11 agent 是否已装到本机 | ✅ 全装完成 | DB 持久化 + fs.copyFileSync，重启 3 次验证保持 |

### 本轮（2026-10-31 下午）修复快照 ✅
| 问题 | 修复 | 状态 |
|---|---|---|
| db.ts 启动删旧 DB（设计缺陷） | **C 方案完整迁移层**：dbNeedsInit→幂等建表+seed upsert+PRAGMA table_info 列级补列 | ✅ 重启 3 次 installed=11 保持 |
| web/src/ 类型错误（5 处） | vue-tsc Booleanish 严格：`:disabled="!!x"` + emit 签名带 payload + runtime_status cast | ✅ vue-tsc --noEmit → 0 错误 |
| web/tsconfig.json 缺失 | 补 tsconfig.json + tsconfig.node.json + src/env.d.ts | ✅ build 配置齐 |
| Vite port 残留 5173 | taskkill PID 3764，Vite 回到稳定 5173 | ✅ |

---

## 三、启动/停止命令（接手必知）

```powershell
# 后端（在 server/ 目录）
cd server; npx tsx src/index.ts
# → http://localhost:3001

# 前端（在 web/ 目录）
cd web; npm run dev
# → http://localhost:5173

# 重扫安装状态（后端运行时）
curl -X POST http://localhost:3001/api/agents/scan-all

# 一键全装 11 个
curl -X POST http://localhost:3001/api/agents/install-all
```

---

## 四、关键路径（新 AI 接手必读文件）

### 后端（4 个源文件）
```
server/src/
├── index.ts        ← Express app + ws server 入口
├── db.ts           ← SQLite schema + C 方案完整迁移层（幂等建表 + seed upsert + PRAGMA table_info 列级补列）
│   ✅ 已修复：启动绝不删库（见第五节踩坑 #3）
├── ws.ts           ← WebSocket broadcast
└── routes/agents.ts ← 所有 CRUD + install management + governance 逻辑
    ├── GET    /api/agents             列表 + summary
    ├── GET    /api/agents/:name        单个详情
    ├── POST   /api/agents/:name/install    单个安装（Node fs.copyFileSync）
    ├── POST   /api/agents/:name/uninstall  单个卸载（fs.unlinkSync + rmSync）
    ├── POST   /api/agents/:name/scan       更新单个安装状态
    ├── POST   /api/agents/:name/status     更新运行状态 busy/idle
    ├── POST   /api/agents/scan-all         全扫 11 个 + 更新 DB + 广播
    ├── POST   /api/agents/install-all      全装 11 个（循环调 findAgentSourceDir + copyFileSync）
    ├── POST   /api/agents                  新增（带 runGovernanceCheck 5 条门槛）
    ├── PUT    /api/agents/:name            编辑（DB + 同步写 agent.md）
    └── DELETE /api/agents/:name            删除（DB + 源目录 + 安装文件 全清）
```

### 前端（12 个源文件 · vue-tsc --noEmit 0 错误 ✅）
```
web/
├── tsconfig.json           ← Vue + Vite + TS config（补建）
├── tsconfig.node.json      ← vite.config.ts 编译（补建）
└── src/
    ├── main.ts             ← 入口
    ├── env.d.ts            ← .vue 模块声明（补建）
    ├── style.css           ← 全局 CSS 变量
    ├── App.vue             ← 主布局 + 顶栏（新增/全扫/全装）+ 批量结果面板 + Create/Edit Modal 控制 + 删除确认
    ├── api/
    │   ├── agents.ts       ← 所有 API 方法 + CreateAgentPayload/GateCheck 类型
    │   └── ws.ts           ← WebSocket client（scan/runtime/install 三类事件）
    └── components/
        ├── AgentSidebar.vue    ← 左侧花名册（状态灯 + emoji + 名字）
        ├── AgentDetail.vue     ← 右侧详情（8 个 section + 🔧员工管理按钮区）
        ├── AgentForm.vue       ← 新增/编辑表单组件（管制门槛 5 条可视化 PASS/FAIL）
        └── SearchBar.vue       ← 搜索筛选（P0/P1 + installed + keyword）
```

### 管制协议（AGENTS.md）
```
AGENTS.md（根目录）
├── ④-补 新增员工管制协议（强制门槛 · 不过不许写 agent.md）
│   ├── 门槛 1：英文名唯一（DB + 源目录 真查）
│   ├── 门槛 2：能力补位（工具交集 + 角色关键词 粗比对）
│   ├── 门槛 3：完整 Skill 支撑（role≥10字 + trigger/boundary 非空）
│   ├── 门槛 4：优先级 P0/P1 enum 校验
│   ├── 门槛 5：英文名 kebab-case 正则
│   └── POST /api/agents → 422 带 gates 数组 → 前端渲染红色 FAIL 色条
├── ⑥ 三色边界灯（🚫 绝对禁止 2 条）
└── ⑦ 变更日志（REG-CRUD-001 ~ REG-INSTALL-001 等）
```

### 安装脚本（5 个）
```
scripts/
├── install-single-agent.ps1     ← 单个安装（PowerShell）
├── install-all-agents.ps1       ← 一键全装
├── scan-install-status.ps1      ← 重扫安装状态 + 写 agent-install-status.json
├── pre-check-new-agent.ps1      ← 新增员工门槛 1a 目录冲突检查
└── agent-proposal-template.md   ← 新增员工提案模板
⚠️ 但后端 install/uninstall/scan-all 已改用 Node 原生 fs 实现（绕开 PowerShell 中文路径编码坑）
   PowerShell 脚本保留给手动用，后端 API 不走它们
```

---

## 五、踩坑教训（别重蹈覆辙）

### 1. PowerShell 5 对中文路径的 UTF-8 不认
- **现象**：Node spawn PowerShell -File `"团队工作台"` → 报 `-File 参数不是有效 .ps1 文件`
- **尝试**：-Command 模式 / 8.3 短路径（cmd.exe 被沙箱拦） / .NET P/Invoke GetShortPathName（分区没开 8.3）
- **解法**：**用 Node 原生 fs 操作绕开**（copyFileSync / existsSync / mkdirSync / unlinkSync / rmSync）
- **铁律**：只要路径里有中文，PowerShell 就是雷区。Node 原生 fs 是唯一稳定解法

### 2. PROJECT_ROOT 路径层级算错
- **现象**：routes 在 `server/src/routes/`，`path.resolve(__dirname, "..", "..")` 只到 `server/`
- **解法**：`path.resolve(__dirname, "..", "..", "..")` 上三层才到项目根
- **铁律**：任何 findAgentSourceDir / scan-all / install-all 都依赖 PROJECT_ROOT，改目录结构必须同步改这个

### 3. db.ts 启动时删旧 DB 重建 ✅ 已修复（2026-10-31）
- **旧设计**：`if (fs.existsSync(dbPath)) { fs.unlinkSync(dbPath); }` 硬删 → 重启归零
- **根因**：没有 schema 迁移层，把 schema_version 不匹配当成删库理由（过度防御）
- **修复方案**：**C 方案完整迁移层**
  1. `dbNeedsInit()` → 彻底删除，启动绝不删库
  2. `ensureSchema()` → 幂等建表（CREATE TABLE IF NOT EXISTS）
  3. `migrateAgentsTable()` → PRAGMA table_info + ALTER TABLE ADD COLUMN 列级补列
  4. seed 改 `INSERT OR IGNORE` → 已存在的 agent 完全不动
- **验证**：重启 3 次 GET /api/agents → installed=11 保持 ✅
- **铁律**：ExperienceRecall #100005316 迁移经验落地 —— 把删库重建改成幂等迁移

### 4. Node child_process spawn 子表达式引号匹配
- 嵌套引号容易配错，拆成单次调用

### 5. Experience Recall 必调
- 本项目 PowerShell → Node fs 的切换就是 recall 经验给的方向
- 下次改代码前先拉 recall 再动手

---

## 六、待办列表（按优先级）

### 🔴 P0 · 必修（设计缺陷）— 全部已解决 ✅

| # | 问题 | 修复方案 | 状态 |
|---|---|---|---|
| 1 | db.ts 启动删旧 DB → 安装状态每次重启归零 | **C 方案完整迁移层**（幂等建表 + seed upsert + PRAGMA 列级补列） | ✅ 已修复（重启 3 次验证） |
| 2 | findAgentSourceDir 只搜 PROJECT_ROOT 下的目录 | 加 fallback：如果专岗工作台/02_AGENTS 不存在，提示用户手动装 | 🟡 低优先级（当前目录齐） |

### 🟡 P1 · 功能完善 — #3 实际已就绪（handoff 陈旧）

| # | 任务 | 说明 | 状态 |
|---|---|---|---|
| 3 | ~~Vite dev server 端口固定 + 代理 WebSocket~~ | ❌ handoff 陈旧：vite.config.ts **已经有** /api 代理 + /events ws 代理 | ✅ 已就绪 |
| 4 | Sidebar 加编辑/删除按钮（和详情区对称） | 现在只有详情区有，App.vue 里已删掉 Sidebar 的 @delete/@edit emit（因 Sidebar 没 emit） | 🟡 待实现 |
| 5 | 顶栏加 WebSocket 连接状态指示灯 | 连接中/已断开/重连中 | 🟡 待实现 |
| 6 | 管制门槛 G2 抢活检测更详细 | 加具体撞了哪些关键词 | 🟡 待实现 |

### 🟢 P2 · Phase 2/3

| # | 任务 | 说明 |
|---|---|---|
| 8 | Phase 2：AutoGen 风格调用链拖拽画布 | 从现有 depends_on 字段渲染 Agent Graph |
| 9 | Phase 3：xterm.js 内嵌终端 | 面板里点安装 → 内嵌终端跑脚本 |
| 10 | GitHub 集成 | 员工面板加 GitHub star 数 + 最近 commit（接 MCP github 工具） |

---

## 七、管制协议（不可绕过）

新增员工必须过 5 条门槛（后端 runGovernanceCheck），任何一条 FAIL → POST /api/agents 返回 **HTTP 422** + gates 数组，前端 AgentForm 渲染红色 FAIL 色条。**不可能绕过**（不是前端禁按钮，是后端硬拦）。

---

## 八、本轮对话产出清单

| 台账 | 产出 |
|---|---|
| REG-FRONTEND-001 | v3.0 MVP 全栈（Node/Express/SQLite/Vue3/WebSocket） |
| REG-POLISH-001 | 打磨 MVP（8 section 详情 + 调用链 + 工具/边界） |
| REG-TOPBAR-001 | 顶栏加全装/全扫按钮 + 批量结果折叠面板 |
| REG-ZTIER-001 | Z 档安装管理（install/uninstall/scan-all 纯 Node fs） |
| REG-CRUD-001 | A 档 CRUD（POST/PUT/DELETE + 管制协议 5 条门槛） |
| REG-INSTALL-001 | 11 个 agent 全装成功（真跑验证） |
| REG-GOV-001 | AGENTS.md 管制协议 5 条门槛 + 三色边界灯 |
| REG-DB-PERSIST-001 ✅ | **C 方案完整迁移层**：db.ts 启动绝不删库 + seed upsert + PRAGMA 列级补列（重启 3 次验证 installed=11 保持） |
| REG-VUE-TSC-001 ✅ | vue-tsc --noEmit 5 处类型错误修复（Booleanish 严格 + emit 签名 + runtime_status cast） → **0 错误** |
| REG-TSCONFIG-001 ✅ | web/tsconfig.json + tsconfig.node.json + src/env.d.ts 补建 → build 配置齐 |
| REG-MVP-DELIVER-001 ✅ | **v3.0 MVP 全绿交付**：后端 11 API + DB 持久化 + ws 广播 + 前端 12 源文件 + 浏览器 5/5 PASS + 控制台零错误 |

---

## 九、快速验证命令（接手后先跑一遍）

```powershell
# 1. 健康检查
Invoke-RestMethod -Uri "http://localhost:3001/health" -UseBasicParsing

# 2. 11 agent 列表
Invoke-RestMethod -Uri "http://localhost:3001/api/agents" -UseBasicParsing

# 3. 重扫安装状态
Invoke-RestMethod -Uri "http://localhost:3001/api/agents/scan-all" -Method POST -UseBasicParsing

# 4. 全装
Invoke-RestMethod -Uri "http://localhost:3001/api/agents/install-all" -Method POST -UseBasicParsing

# 5. 新增员工（测管制）
$body = '{"name":"test-agent","cn_name":"测试员","priority":"P1","role":"x"*20,"how_to_trigger":{"manual":["m"],"auto":["a"]},"boundary":["b"]}'
try { Invoke-RestMethod -Uri "http://localhost:3001/api/agents" -Method POST -ContentType "application/json" -Body $body -UseBasicParsing } catch { $_.Exception.Response.StatusCode }

# 6. 浏览器打开面板
Start-Process "http://localhost:5173/"
```

---

## 十、设计层 vs 执行层边界（新 AI 必读）

- **AGENTS.md**（根目录）= 项目设计层：技术栈/文件地图/管制协议/三色边界灯
- **1号指令**（user_rules）= 执行层：铁律/四件套/格式规范/编号体系
- 两层有冲突时 → **AGENTS.md 说了算**（铁律 #25）
- 管制协议写在 AGENTS.md 里，后端 runGovernanceCheck 只是"执行"它，不是"定义"它

---

## 总结

> ✅ **v3.0 MVP 全绿交付**（2026-10-31 下午）。
>
> 后端 Node/Express/SQLite/ws 11 路由全就绪 + **db.ts C 方案完整迁移层**（重启 3 次 installed=11 保持）+ WebSocket /events 广播；
> 前端 Vue3 12 源文件 + vue-tsc --noEmit **0 错误** + 搜索筛选 + 管制协议 5 条可视化 PASS/FAIL + WebSocket 3 事件实时更新状态灯；
> 浏览器 5 项全 PASS（11 agent sidebar + 点击切换详情 + 全扫 + 全装 + 控制台零错误）。
>
> **阻塞缺陷已全部解决**：db.ts 删库（#1 ✅）+ Vue 类型错误（✅）+ tsconfig 缺失（✅）。
>
> 下一步：Phase 2 调用链拖拽画布 / Phase 3 xterm.js 内嵌终端 / 打磨（Sidebar 编辑按钮 / ws 状态灯 / G2 抢活关键词明细）。
