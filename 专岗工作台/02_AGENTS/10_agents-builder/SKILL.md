---
name: agents-builder
description: AGENTS.md 构建员专属知识手册。侦查 SOP（扫 package.json→读 README→查 git log→推断技术栈）+ 六块区标准结构 + 嵌套规则。
---

# AGENTS.md 构建员 · 专属知识手册

## 铁律原文（必引用）

铁律 #25：只在聊天里说过没登记的 = 等于没发生过（AGENTS.md 是跨会话接力的唯一依据）
铁律 #11：动手前给组合逻辑图（AGENTS.md 就是"做什么"和"在哪做"的逻辑图）
核心边界：AGENTS.md 只管"做什么"和"在哪做"（设计层），不管"怎么做"（执行层）

## 侦查 SOP（五步法，必须真读文件）

```
Step 1 扫 package.json / pyproject.toml / go.mod / Cargo.toml
→ 读依赖清单，提取：
  语言版本 / 框架版本 / 包管理器 / 构建工具
→ 读 scripts 块，提取：
  装依赖 / 启动 / 测试 / 构建 命令
→ 读 dependencies 和 devDependencies 区分生产/开发

Step 2 读 README.md
→ 这是人类文档，AGENTS.md 不重复 README 写过的
→ 重点抓：项目定位 / 快速开始 / 架构说明 / 常见问题
→ 如果 README 里有启动命令 → 和 package.json 里的对比（看哪个是对的）

Step 3 扫目录结构
→ 列出顶层目录和 src/ 下一级
→ 识别：
  主入口文件（main / index / App）
  核心业务模块（按功能分目录的）
  配置文件（config / .env / constants）
  测试目录（test / __tests__ / spec）

Step 4 查 git log + .gitignore
→ git log --oneline -20：
  commit 风格（规范 / 随意）
  维护者活跃度（最近 commit 时间）
  有没有 WIP / fixme / temp 之类的痕迹
→ .gitignore：
  列全排除清单（这些就是安全红线来源）

Step 5 推断技术栈 + 补全
→ 结合以上信息，写技术栈总览表：
  语言 / 框架 / 数据库 / ORM / 构建工具 / 测试框架 / 包管理器
→ 不确定的 → 联网查官方文档补全
→ 跑一遍启动命令验证（能真跑就真跑）
```

## AGENTS.md 六块区标准结构

### 块 1：项目简介（业务层）

```markdown
## 项目简介
- 一句话描述：{这项目是干嘛的}
- 核心目标：{解决什么问题}
- 用户画像：{谁在用}
```

### 块 2：技术栈总览（技术层）

```markdown
## 技术栈总览
| 类别 | 选型 | 版本 | 备注 |
|---|---|---|---|
| 语言 | TypeScript | 5.x | |
| 框架 | Next.js | 14.x | App Router |
| 数据库 | PostgreSQL | 16.x | |
| ORM | Prisma | 5.x | |
| 构建 | Turbopack | | |
| 测试 | Vitest | | |
| 包管理 | pnpm | 8.x | |
```

### 块 3：构建运行（工程层）

```markdown
## 构建运行
| 操作 | 命令 | 说明 |
|---|---|---|
| 装依赖 | `pnpm install` | |
| 启动开发 | `pnpm dev` | 默认端口 3000 |
| 跑测试 | `pnpm test` | Vitest |
| 构建生产 | `pnpm build` | 产物在 .next/ |
| 启动生产 | `pnpm start` | |
```

**硬约束**：能真跑就真跑一遍再写，别瞎写。

### 块 4：代码规范（约定层）

```markdown
## 代码规范
- 命名：
  - 变量/函数：camelCase
  - 文件/目录：kebab-case 或 camelCase（从现有代码提取）
  - 类/组件：PascalCase
- 缩进：2 空格 / 4 空格（从现有代码提取）
- Linter：ESLint + Prettier（有 .eslintrc / .prettierrc 吗？）
- 目录结构：
  - src/components/ → UI 组件
  - src/lib/ → 工具函数
  - src/pages/ → 页面
- 注释风格：从现有代码看（// 还是 /* */，写不写 JSDoc）
```

### 块 5：安全红线（禁止层）

```markdown
## 安全红线
- .gitignore 排除的文件绝对不能提交：
  - .env / .env.local / .env.production
  - *.pem / *.key / secrets/
  - node_modules/ / .next/ / dist/
- 配置密钥绝对不能硬编码进代码（走环境变量）
- 绝对不能碰的目录/文件：
  - config/production/（生产配置）
  - migrations/（数据库迁移，改了要回滚脚本）
- 测试数据和生产数据严格隔离
```

### 块 6：架构模式 + 子模块导航（设计层）

```markdown
## 架构模式
### 模块边界
```
App Router（Next.js）
  ├─ src/app/ → 页面路由
  ├─ src/components/ → 共享 UI
  ├─ src/lib/ → 工具/API 封装
  └─ src/server/ → 服务端逻辑（只在服务器跑）
```

### 外部依赖
- PostgreSQL（Prisma ORM）
- Redis（缓存，可选）
- Stripe（支付）

## 子模块导航
| 目录 | 职责 | 建议 |
|---|---|---|
| src/app/ | 页面路由 | 独立模块，每个子路由可以有自己的 AGENTS.md |
| src/components/ | UI 组件库 | |
| src/lib/ | 通用工具 | |
```

## 嵌套规则（大项目自动嵌套）

```
判断标准：
→ 小项目（<5 模块，或全部代码在一个 src/ 下）：
  → 一个根 AGENTS.md 搞定
→ 中等项目（monorepo / 3-10 个独立子项目）：
  → 根 AGENTS.md（总纲 + 公共规则）
  → 每个子项目独立 AGENTS.md（子目录里）
→ 大项目（10+ 模块，子模块有独立业务逻辑）：
  → 根 AGENTS.md（总纲）
  → 一级子目录各一份 AGENTS.md
  → 深到三级模块也可以有
```

**嵌套子文件开头必须写**（给 AI 自动拼接上下文用）：

```markdown
> 继承根 {项目根}/AGENTS.md 的所有规则
> 本文件只补充 / 覆盖本子目录特有的约定
```

## 输出模板：AGENTS.md 构建结果

```
═══ AGENTS.md 构建结果 ═══
├─ 输出文件：
│   ├─ {项目根}/AGENTS.md（✅ 新建 / ✅ 更新 / ✅ 增量补充）
│   └─ {子目录}/AGENTS.md（✅ 新建，共 N 个子 AGENTS）
├─ 六块区覆盖：
│   ├─ ✅ 项目简介 / 技术栈总览
│   ├─ ✅ 构建运行（真跑通 / ❌ 跑不通 → 原因）
│   ├─ ✅ 代码规范（从现有代码提取）
│   ├─ ✅ 安全红线（从 .gitignore + 代码扫描）
│   ├─ ✅ 架构模式
│   └─ ✅ 子模块导航 / 嵌套建议
├─ 从现有代码提取的约定：{命名风格/缩进/注释}
├─ 联网补全的外部规范：{框架最新版本/官方文档}
├─ 真跑验证：{启动命令真跑通过 / ❌ 报错}
└─ 侦查来源：{读了 package.json + README + .gitignore + 目录结构}
```

## 必查表

| 场景 | 必须问 |
|---|---|
| 新建 AGENTS | 六个块区覆盖了吗？一个不能少。 |
| 更新已有 | 是增量补充还是整体覆盖？（必须增量） |
| 构建命令 | 这个命令真跑过吗？能跑通吗？ |
| 安全红线 | .gitignore 全读完了吗？有没有漏？ |
| 嵌套 | 哪些子目录值得建独立 AGENTS.md？ |
| 技术栈 | 没读到依赖清单就写了技术栈？（不许！） |
