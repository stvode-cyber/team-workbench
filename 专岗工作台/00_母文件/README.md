# 专岗工作台 · v2.0

> 1号指令母版配套 · 生成时间 2026-10-30
> 核心改动：每个 Agent 独立目录 + 专属 Skill 内嵌 + 边界清晰

## 一句话

这是一个**给 AI 用的专岗开发框架**——每个 Agent 有自己的目录、自己的专属 Skill、自己的 README。你装的时候是"装一个 Agent 包"，不是"装一堆零散文件"。

## 目录结构

```
专岗工作台/
├── 00_母文件/                    ← 总说明 + 开源生态清单
├── 01_总览面板/                   ← 可视化面板（HTML）
├── 02_AGENTS/                     ← 每个 Agent 一个独立目录
│   ├── 01_drize-engineer/          ─┐
│   │   ├── agent.md                  ├─ 三件套：
│   │   ├── SKILL.md                  │  agent（角色） + Skill（知识） + README（说明）
│   │   └── README.md                 ─┘
│   ├── 02_arch-designer/
│   │   ├── agent.md + SKILL.md + README.md
│   ├── ...（11 个 Agent，每个目录都三件套）
│   └── 11_mcp-finder/
└── 03_开源生态/                   ← 精选的第三方 Skill/MCP 清单
    ├── Skill-精选.md
    ├── MCP-精选.md
    └── Hook-模板.md
```

## Agent 目录三件套说明

| 文件 | 格式 | 放哪 | 作用 |
|---|---|---|---|
| `agent.md` | Subagent .md（YAML frontmatter + prompt） | 复制到 `{项目}\.trae\agents\` 或 `%userprofile%\.trae-cn\agents\` | 角色定义：谁、干什么、什么时候被调、硬性边界 |
| `SKILL.md` | Trae Skill 格式（可选 YAML + 正文） | 复制到 `{项目}\.trae\skills\{agent-name}\` 或 `%userprofile%\.trae-cn\skills\{agent-name}\` | 专属知识：这个 Agent 独有的 SOP、最佳实践、检查清单 |
| `README.md` | 纯 Markdown | 留在工作台当参考 | 使用说明：怎么装、怎么触发、输出长啥样 |

## 边界规则（为什么分三个文件）

```
agent.md = 我是谁（角色）   ← Subagent 格式，主 AI 自动调
SKILL.md = 我懂什么（知识） ← Skill 格式，按需加载不占 token
README.md = 怎么用我（文档）← 给人看的
```

**严格边界**：
- agent.md 里不许塞知识细节（塞多了会污染主对话上下文）
- SKILL.md 里不许塞角色定义（塞多了 Skill 就不是 Skill 了）
- README.md 只给人看，AI 不自动读
- 知识跨 Agent 共享 → 不允许。每个 Agent 独享自己的 Skill

## 快速安装（以推演引擎为例）

```powershell
# 第 1 步：复制 agent.md 到 agents 目录
Copy-Item 02_AGENTS\01_drize-engineer\agent.md ^
  %userprofile%\.trae-cn\agents\drize-engineer.md

# 第 2 步：复制 SKILL.md 到 skills 目录
New-Item -ItemType Directory %userprofile%\.trae-cn\skills\drize-engineer -Force
Copy-Item 02_AGENTS\01_drize-engineer\SKILL.md ^
  %userprofile%\.trae-cn\skills\drize-engineer\SKILL.md

# 第 3 步：Trae 设置 → Skills → 导入（或重启 Trae 自动识别）
# 完事，下次 AI 自动就有推演引擎能力了
```

## 11 个 Agent 清单

| # | 目录名 | 中文名 | 优先级 | 核心能力 |
|---|---|---|---|---|
| 1 | drize-engineer | 推演引擎 | 🔴 P0 | 正推+反推+失败轨道+竞品五张牌 |
| 2 | arch-designer | 架构师 | 🔴 P0 | 01-03 层架构+模块边界+技术选型 |
| 3 | project-taker | 接手员 | 🔴 P0 | 扫家底+画像+补 AGENTS.md |
| 4 | ledger-keeper | 台账登记员 | 🔴 P0 | 开工/动手/收尾自动登记 |
| 5 | rca-analyst | RCA 根因分析师 | 🟡 P1 | 五步根因+吸收+回归测试 |
| 6 | competitor-surveyor | 竞品调研员 | 🟡 P1 | 5 竞品实据+失败案例 |
| 7 | backend-expert | 后端专家 | 🟡 P1 | API+DB+Auth+Observability |
| 8 | safety-auditor | 安全巡视员 | 🟡 P1 | 版本对齐+硬编码+别碰文件 |
| 9 | report-writer | 汇报出图员 | 🟡 P1 | 三层报告+项目进程+对话卡 |
| 10 | agents-builder | AGENTS.md 构建员 | 🟡 P1 | 侦查→生成→嵌套 AGENTS.md |
| 11 | mcp-finder | MCP 搜索员 | 🟡 P1 | 联网搜 4 来源+安装指引 |
