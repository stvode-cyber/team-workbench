# 跟单员（ledger-compiler）· 使用说明

## 它是啥

台账编译器。把散的台账条目编译成一张**项目索引卡**，让 AI 一眼知道状态，不用翻完整个 CHANGELOG.md。

## 为什么需要它

没有跟单员时：
```
AI 想知道"这个项目现在啥情况"
→ 翻 CHANGELOG.md（可能 500 行）
→ 一条一条找哪些是待办、哪些已完成、哪些阻塞
→ 消耗 3-5 轮对话上下文
→ 信息还不全（可能漏了最新的）
```

有跟单员后：
```
AI 想知道"这个项目现在啥情况"
→ 读 .trae/index-card.md（通常 < 50 行）
→ 一眼看到：
   🎯 M2 进度 60%
   📋 5 条待办
   🚧 2 个阻塞（SQLite 不兼容）
   ⚠️ 3 个风险
   📌 下一个目标：先解决 SQLite
→ 想看某个阻塞的详情？搜 RCA-003 → 直接找到原台账
→ 消耗 1 轮对话，信息全
```

## 和 04_ledger-keeper 的区别

| 维度 | 04_ledger-keeper | 14_ledger-compiler |
| ---- | ---------------- | ------------------ |
| 角色 | 登记员（流水账） | 跟单员（索引卡） |
| 触发 | Hook 被动触发（开工/动手/收尾） | 需要时被调起（台账膨胀/对话拉长/指南针校准） |
| 产出 | 散的台账条目 | 结构化索引卡 |
| 类比 | 收银机（每笔都记） | 会计（月底编报表） |

## 什么时候调它

- 用户说"汇总一下"、"当前状态"、"生成项目卡"
- CHANGELOG.md 超过 50 行 / 台账 ≥ 20 条
- 一个对话超过 10 轮（上下文太长）
- 总负责人指南针校准的时候

## 安装步骤

### 第 1 步：复制角色到用户级 agents 目录

```powershell
New-Item "$env:USERPROFILE\.trae-cn\agents" -Force

Copy-Item `
  "c:\Users\Administrator\Desktop\Agent\团队工作台\团队\14_ledger-compiler\agent.md" `
  "$env:USERPROFILE\.trae-cn\agents\ledger-compiler.md"
```

### 第 2 步：复制专属知识到用户级 skills

```powershell
New-Item "$env:USERPROFILE\.trae-cn\skills\ledger-compiler" -Force

Copy-Item `
  "c:\Users\Administrator\Desktop\Agent\团队工作台\团队\14_ledger-compiler\SKILL.md" `
  "$env:USERPROFILE\.trae-cn\skills\ledger-compiler\SKILL.md"
```

### 第 3 步：重启 Trae / 重新加载

重启 Trae，或者在 Trae 设置里点「Skills → 重新加载」。

## 快速验证

装完后在一个有 CHANGELOG.md 的项目目录里说：

```
帮我汇总一下当前项目状态
```

预期结果：
1. 跟单员被调起
2. 读 CHANGELOG.md + 项目进程.md
3. 生成六大版块索引卡
4. 输出到 `.trae/index-card.md`
5. 汇报给你

## 硬性边界

- ❌ 不删散台账（只编译，不删除）
- ❌ 不代替 04_ledger-keeper（登记员负责写条目，你负责编译）
- ✅ 允许：读全量台账、生成索引卡、更新索引卡、按编号下钻

## 输出示例

```
═══ GreenRhino · 索引卡 v3 ═══
├─ 项目代号：greenrhino
├─ 当前阶段：M2 核心功能 [>]
├─ 最后更新：2026-10-31 16:20
└─ 总台账数：27 条

├─ 🎯 里程碑进度
│   ├─ M1 架构搭建 ██████████░ 90% → REG-003
│   ├─ M2 核心功能 ██████░░░░ 60% → 进行中
│   └─ M3 联调上线 ░░░░░░░░░░  0% → 未启动

├─ 📋 待办清单（5）
│   ├─ [>] 加 SQL 索引检查 Hook → REG-015
│   ├─ [ ] 写登录页单元测试 → REG-016
│   ├─ [ ] 优化首屏加载速度 → REG-017
│   ├─ [ ] 补 README 安装文档 → REG-018
│   └─ [ ] 升级 Tauri 到 2.0 → REG-019

├─ 🚧 阻塞问题（2）
│   ├─ [!] SQLite 版本不兼容 → RCA-003
│   └─ [!] 图标设计稿未交付 → IMP-011

├─ ✅ 已完成（近 10 条）
│   ├─ [x] M1 架构搭建完成 → REG-003
│   ├─ [x] 数据库迁移脚本 → REG-012
│   ├─ [x] 登录鉴权流程 → REG-014
│   └─ ...

├─ ⚠️ 风险点（3）
│   ├─ 推演报 SSR 加 Vue 冲突 → DZ-007
│   ├─ Tauri 2.0 升级可能破坏打包脚本 → DZ-009
│   └─ ...

└─ 📌 下一个目标
    ├─ 1. 先解决 SQLite 阻塞（RCA-003）
    ├─ 2. 加 SQL 索引检查 Hook（REG-015）
    └─ 3. 继续 M2 核心功能
```

## 按需下钻（想看详情）

```powershell
# AI 直接跑：
Select-String "RCA-003" CHANGELOG.md

# 或者你手动查：
findstr /n "RCA-003" CHANGELOG.md
```

## 团队目录现在 14 个

```
01_drize-engineer          推演引擎
02_arch-designer           架构师
03_project-taker           接手员
04_ledger-keeper           台账登记员（散条目）
05_rca-analyst             RCA 根因分析师
06_competitor-surveyor     竞品调研员
07_backend-expert          后端专家
08_safety-auditor          安全巡视员
09_report-writer           汇报出图员
10_agents-builder          AGENTS.md 构建员
11_mcp-finder              MCP 搜索员
12_project-pm-template     项目负责人模板
13_global-coordinator      总负责人（指南针）
14_ledger-compiler         跟单员（索引卡编译）🆕
```
