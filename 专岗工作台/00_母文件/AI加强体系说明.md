# Trae AI 加强体系 · 大白话说明

> 本文档整合自 2026-10-30 讨论内容。一句话版本：**规则注入改行为、Skill 给知识、Subagent 派专岗、MCP 装工具、Hook 设自动闹钟。**

---

## 基础模型（先记这个）

AI 本质上就是"一个会说话的大脑"：

| 东西 | 类比 | 是什么 |
|---|---|---|
| AI | 大脑 | 核心引擎 |
| 规则注入（user_rules / .trae/rules/） | 大脑里的底线思维 | 改 AI 的行为规矩 |
| Skill（SKILL.md） | 大脑里的知识手册 | 告诉 AI 某类事怎么干 |
| AGENTS.md | 项目说明书 | 告诉 AI 这个项目的设计层约束 |
| Subagent（.md 专岗） | 派出去的专家 | 主 AI 遇到合适场景自动调它 |
| MCP Server | 手脚（装外设） | 给 AI 新工具能力 |
| Hook | 自动闹钟 | 到特定时刻自动触发动作 |

---

## 第一层：规则注入（L1 · 底层，零成本）

**是什么**：纯文本规则，AI 每次对话自动读。写一次到处生效。

**放哪**：
- 全局（所有项目）：`%userprofile%\.trae\user_rules\`（1号指令就在这层）
- 项目（当前项目）：`{项目根}\.trae\rules\`（支持 3 层嵌套）

**能加强什么**：
- 改 AI 的说话格式（树结构 / 全简体中文 / 四件套）
- 改 AI 的行为规矩（铁律 71 条）
- 改 AI 的默认做事顺序（先推演再动手）

**限制**：纯文本规矩，不能让 AI 获得新工具能力。

---

## 第二层：Skill（L2 · 知识手册）

**是什么**：SKILL.md = 领域知识 + SOP + 可选脚本。智能体按需加载，不是每次都读（省 token）。

**放哪**：
- 全局：`%userprofile%\.trae-cn\skills\{skillname}\SKILL.md`
- 项目：`{项目根}\.trae\skills\{skillname}\SKILL.md`

**能加强什么**：
- 告诉 AI 怎么做某类事（推演引擎 Skill = 推演 SOP）
- 附脚本让 AI 能直接跑（agents-md-generator 带 PowerShell 脚本）
- 附模板让 AI 按模板产出（report-template 带汇报格式模板）

**和 Subagent 的区别**：
- Skill = 知识手册（告诉 AI 怎么干）
- Subagent = 专属角色（主 AI 调它出来干，独立上下文）

---

## 第三层：Subagent 专岗（L3 · 派角色）

**是什么**：Subagent 是通过 .md 文件定义的专用智能体。内置主 Agent 识别到合适任务时自动调它。每个 Subagent 有独立上下文，不污染主对话。

**放哪**：
- 全域（所有项目能用）：`%userprofile%\.trae-cn\agents\{name}.md`
- 项目（只当前项目）：`{项目根}\.trae\agents\{name}.md`

**文件格式**：
```markdown
---
name: 英文标识（必须 kebab-case，如 drize-engineer）
description: |
  中文描述。写清什么时候调我 + 能干什么。
  Use this agent when:
  - 场景 1
  - 场景 2
tools: ['read', 'search', 'web_search']    # 可选
mcp-servers: ['github', 'playwright']        # 可选
---

# 角色（中文）
你是谁，按什么规矩干活，输出长啥样。
```

**当前已建 11 个专岗**：
- drize-engineer（推演引擎）
- arch-designer（架构师）
- project-taker（接手员）
- ledger-keeper（台账登记员）
- rca-analyst（RCA 根因分析师）
- competitor-surveyor（竞品调研员）
- backend-expert（后端专家）
- safety-auditor（安全巡视员）
- report-writer（汇报出图员）
- agents-builder（AGENTS.md 构建员）
- mcp-finder（MCP 搜索员）

**Trae 限制**：自定义 Subagent 之间不能互相调，只能内置主 Agent 调 Subagent。

---

## 第四层：MCP Server（L4 · 装工具，质变层）

**是什么**：Model Context Protocol（模型上下文协议），国际通用的"外设插座标准"。装一个 MCP = 给 AI 加一种新工具能力。

**类比**：
- AI 是一台电脑
- MCP 协议 = USB 接口标准
- MCP Server = 插在 USB 上的外设（鼠标、键盘、U盘……）

**能装什么 MCP**：
| MCP 类型 | 能干啥 | 例子 |
|---|---|---|
| GitHub MCP | 查 commit、提 PR、建 issue | 自动读仓库信息 |
| Playwright MCP | 控制浏览器 | 打开网页、点按钮、截图 |
| SQLite/PostgreSQL MCP | 直接连数据库查数据 | 真跑 SQL 拿结果 |
| Figma MCP | 读 Figma 设计稿 | 自动提设计 token |
| Windows Filesystem MCP | 操作 Windows 文件系统 | 批量移动、解压、重命名 |
| Notion MCP | 读写 Notion 文档 | 自动同步知识库 |
| Slack MCP | 发 Slack 消息 | 自动汇报 |
| npm MCP | 查 npm 包信息 | 自动查版本和漏洞 |

**关键认知**：
- MCP 是**提前装**的，装一次永久生效
- AI **自动调用**，你不用指定"用哪个 MCP"
- 你只说要什么结果，它自己判断用哪个工具最合适
- MCP 装得越多 → AI 能选的工具越多 → 结果越准

**装 MCP 的通用步骤**：
1. Trae → 设置 → MCP Server → 添加
2. 填名称（随便起）+ 启动命令（README 里找，通常是 `npx xxx`）
3. 保存 → 下次对话自动有了

**MCP 在哪找**：
- 官方参考实现：github.com/modelcontextprotocol/servers
- 社区全量清单：github.com/punkpeye/awesome-mcp-servers
- Trae 社区论坛：forum.trae.cn 搜 "MCP"
- 不确定有没有合适的 → 调 **mcp-finder**（已建的 Subagent）联网搜

---

## 第五层：Hook（L5 · 自动闹钟）

**是什么**：钩子（Hook）= 在 AI 执行过程的特定事件节点，自动跑自定义 Shell 命令 / 调 Skill / 拦截操作。

**类比**：
- 闹钟：时间到了自动响（时间 Hook）
- 冰箱灯：门开了自动亮（事件 Hook）
- 手机充电提醒：电量低于 20% 自动弹（条件 Hook）

**AI 执行中能挂 Hook 的节点**：
1. 每次对话开始前
2. AI 加载某个 Skill 后
3. AI 调用某个工具前（比如写文件前）
4. AI 调用某个工具后（比如写文件后）
5. AI 结束对话后
6. 识别到用户说特定关键词时

**Hook 能触发什么动作**：
- 跑一段 Shell 命令（自动跑 lint / 自动跑测试）
- 自动加载某个 Skill
- 自动调某个 Subagent
- 拦截操作（AI 要写 .env → 弹警告）
- 自动追加台账

**你 100% 定的**：
- 触发条件（什么时候触发）
- 触发什么（干什么事）
- 作用域（全域 / 项目）

**配置方式**：
- 方式 1：Trae 设置 → Hooks（图形化界面选）
- 方式 2：写 `.trae/hooks/*.hook.json`（JSON 格式，Trae 自动读）

---

## 5 层组合拳效果对比

| 层级 | 成本 | 维护量 | 效果 | 何时上 |
|---|---|---|---|---|
| L1 规则注入 | 💰 免费 | 🟢 低 | 改 AI 行为 | 必须先上（你已经上了） |
| L2 Skill | 💰 免费 | 🟢 低 | 加领域知识 | 必须先上（你已有 21 个） |
| L3 Subagent | 💰 免费 | 🟡 中 | 派专属角色 | 必须先上（你已有 11 个） |
| L4 MCP | 💻 装服务 | 🟡 中 | **质变：加新工具** | 项目需要精准数据/操作时上 |
| L5 Hook | 💰 免费 | 🟡 中 | 自动化流程 | 有重复性操作要自动化时上 |

---

## 快速对照表

| 你想要 | 用什么 |
|---|---|
| AI 每次说话按树结构 + 四件套 | L1 规则注入（user_rules） |
| AI 按推演 SOP 干活 | L2 Skill（drize-engine） |
| 新项目自动有人画架构 | L3 Subagent（arch-designer） |
| AI 真能查 GitHub star 数 | L4 MCP（GitHub MCP） |
| AI 每次写文件后自动写台账 | L5 Hook |
| 不确定有没有 MCP 能用 | L3 Subagent（mcp-finder） |

---

## 一句话终极总结

> **规则注入改行为 · Skill 给知识 · Subagent 派专岗 · MCP 装工具 · Hook 设闹钟。**
> 五个东西合起来 = 一个有知识 + 有工具 + 能自动 + 有分工的开发团队。
> 单独每个都不强，组合起来指数级加强。

---

*文档生成时间：2026-10-30 | 来源：Trae 官方文档 + GitHub 开源实践 + 今日讨论收敛*
