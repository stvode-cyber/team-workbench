# 总负责人（global-coordinator）· 使用说明

## 它是啥

所有项目的"项目管家"。
比项目负责人高一层，管的是**整个项目生命周期**的创建、接手和方向把控。

## 它和项目负责人的区别

| 维度 | 总负责人 | 项目负责人 |
| ---- | -------- | ---------- |
| 层级 | 全域级（用户级安装） | 项目级（每个项目一个） |
| 数量 | 只有 1 个 | 每个项目各 1 个 |
| 触发 | 新项目启动 / 第一次接手 / 方向把控 | 每次对话都在（常驻） |
| 干什么 | 创建项目负责人、推接手/架构/推演、方向把控、定期汇报 | 管这个项目每天的推进 |
| 类比 | 项目经理的经理 | 这个项目的项目经理 |

## 什么时候调它

- 你说「帮我搭一个 XXX 项目」→ **新项目启动**
- 你说「帮我看看这个项目」→ **第一次接手陌生项目**
- 你说「这个项目方向对吗」→ **方向把控**
- AI 自动检测到项目没负责人 → **补项目负责人**

## 安装步骤

### 第 1 步：复制角色定义到用户级目录

```powershell
# 创建目录（如果还没有）
New-Item "$env:USERPROFILE\.trae-cn\agents" -Force

# 复制 agent.md
Copy-Item `
  "c:\Users\Administrator\Desktop\Agent\团队工作台\团队\13_global-coordinator\agent.md" `
  "$env:USERPROFILE\.trae-cn\agents\global-coordinator.md"
```

### 第 2 步：复制专属知识到用户级 Skills

```powershell
# 创建专属 skills 目录
New-Item "$env:USERPROFILE\.trae-cn\skills\global-coordinator" -Force

# 复制 SKILL.md（注意：SKILL.md 保留 YAML frontmatter）
Copy-Item `
  "c:\Users\Administrator\Desktop\Agent\团队工作台\团队\13_global-coordinator\SKILL.md" `
  "$env:USERPROFILE\.trae-cn\skills\global-coordinator\SKILL.md"
```

### 第 3 步：重启 Trae / 重新加载

重启 Trae，或者在 Trae 设置里点「Skills → 重新加载」。

## 快速验证

装完之后在任意目录（选个新项目或陌生目录）说：

```
帮我搭一个 todo 应用，技术栈 React + Vite
```

预期结果：
1. 总负责人被调起
2. 它确认项目名、代号、技术栈
3. 它在项目目录下创建 `.trae/agents\todo-app-pm.md`
4. 它推送接手员摸底、架构师出架构卡、推演引擎过一遍
5. 它创建《项目进程.md》v0.1
6. 它给你汇报初始化完成

## 强制前提

总负责人**依赖**团队工作台里的 12 号项目负责人模板。
确保以下文件存在：

```
{你的团队工作台}\团队\12_project-pm-template\agent.md
```

如果丢了，从母文件打包目录重新复制一份。

## 硬性边界（别让它越界）

- ❌ 它不写业务代码（那是 07_backend-expert 的活）
- ❌ 它不跳过推演引擎（任何改动前必须过）
- ❌ 它不编造项目状态（所有判断必须有实据）
- ✅ 它管"方向"，不管"干活"

## 输出示例

### 新项目初始化

```
═══ 项目 todo 应用 初始化完成 ═══
├─ 项目负责人：.trae\agents\todo-app-pm.md ✅ 已创建
├─ 技术栈：React + Vite
├─ 初始里程碑：
│   ├─ M1 架构搭建
│   ├─ M2 核心功能
│   └─ M3 联调上线
├─ 当前状态：接手员摸底中 → 推演引擎排队中
└─ 台账 REG-001 已登记
```

### 方向把控

```
═══ 项目 todo 应用 · 方向检查 ═══
├─ 里程碑进度：M2 已过 60%
├─ 风险点：SQLite 查询没加索引，性能可能有问题
├─ 方向判断：✅ 正常（有风险但未偏离）
├─ 建议：调 safety-auditor 加 SQL 索引检查 Hook
└─ 下次检查：完成 M2 后自动触发
```
