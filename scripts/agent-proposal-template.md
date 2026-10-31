# 新增员工提案模板

> 管制协议门槛 2/3/5 的手动填写部分。填完 AI 来跑 pre-check 脚本过门槛 1/4。
> 位置：填完删除（不要留在项目里）

---

## 基本信息

| 项 | 值 |
|---|---|
| 中文名 | （例：发布自动化员） |
| 英文名（agent name） | （例：release-automator · 必须和 agent.md frontmatter `name:` 一致） |
| 优先级 | P0 / P1 |
| 建议编号 | （查 `02_AGENTS/` 现有最大编号 +1，例：12） |

---

## 门槛 2：软冲突分析（能力补位，不抢活）

### 新员工核心能力（一句话）
> 例：safety-auditor 管发版前双检，新员工管发版后自动部署 + 线上监控告警

### 和 11 个已有 Agent 逐一比对
> 按 AGENTS.md 速查表的"核心能力"列逐条比对，写清楚哪里补位、哪里不重叠

| 已有 Agent | 核心能力 | 是否重叠？ | 说明 |
|---|---|---|---|
| drize-engineer | 推演五张牌 | ❌ | 新员工不管推演 |
| arch-designer | 架构设计 | ❌ | 新员工不管架构 |
| project-taker | 接手扫家底 | ❌ | 新员工不管接手 |
| ledger-keeper | 台账登记 | ❌ | 新员工不管台账 |
| rca-analyst | bug 根因 | ❌ | 新员工不管 bug |
| competitor-surveyor | 竞品调研 | ❌ | 新员工不管调研 |
| backend-expert | 后端实现 | ❌ | 新员工不管写 API |
| safety-auditor | 发版前扫描 | ⚠️ 边界 | safety-auditor 管"发不发"，新员工管"发完怎么办" |
| report-writer | 汇报 | ❌ | 新员工不管汇报 |
| agents-builder | AGENTS 生成 | ❌ | 新员工不管建 AGENTS |
| mcp-finder | MCP 搜索 | ❌ | 新员工不管搜 MCP |

### 结论（✅ 补位成功 / ❌ 抢活失败）


---

## 门槛 3：Skill 支撑

### SOP 清单（SKILL.md 将写什么）
> 列 3-5 条具体执行步骤

1. 
2. 
3. 

### 检查清单
> 列 3-5 条必须检查的硬项（可 yes/no 勾选）

- [ ] 
- [ ] 
- [ ] 

### 铁律/规矩依据
> 引用 1号指令铁律编号或已有 Skill 里的原文支撑

- 

---

## 门槛 5：优先级合理性

### 为什么是 P0？（选了 P1 删掉这块）
> 必须满足"改 ≥3 行代码前 / 花钱前 / 不可逆前"三条里的至少一条

- [ ] 会参与改 ≥3 行代码的不可逆动作
- [ ] 涉及花钱/上传/发布等不可逆操作
- [ ] 阻塞主流程（不调它就动不了）

### 为什么是 P1？（选了 P0 删掉这块）
> 可选、不阻塞主流程

- 

---

## 硬冲突（pre-check 脚本自动跑，你不用填）
> 跑 `powershell -File .\scripts\pre-check-new-agent.ps1 -AgentName <name> -CnName <cn> -Priority P1`
