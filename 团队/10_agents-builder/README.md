## 一句话简介

AGENTS.md 构建员专岗：自动侦查项目（读依赖/README/.gitignore/git log）→ 生成六块区 AGENTS.md，大项目自动嵌套子目录。

## 什么时候触发

- 新项目还没有 AGENTS.md
- 你说"帮我建 AGENTS.md"、"生成项目配置"、"项目规范"
- 接手员产出项目画像后要落地成文件
- 现有 AGENTS.md 过时 / 不全

## 输出长啥样（示例）

```
═══ AGENTS.md 构建结果 ═══
├─ 输出文件：
│   ├─ {项目根}/AGENTS.md（✅ 新建）
│   └─ src/app/AGENTS.md（✅ 新建，子目录嵌套）
├─ 六块区覆盖：
│   ├─ ✅ 项目简介：Next.js 图片浏览器，解决本地看图卡慢问题
│   ├─ ✅ 构建运行：装依赖 pnpm i / 启动 pnpm dev / 测试 pnpm test（真跑通 ✅）
│   ├─ ✅ 代码规范：2 空格缩进 / camelCase 变量 / PascalCase 组件（从现有代码提取）
│   ├─ ✅ 安全红线：.gitignore 排除 .env/secrets/*.pem / 配置密钥不许硬编码
│   ├─ ✅ 架构模式：Next.js App Router → Prisma → PostgreSQL
│   └─ ✅ 子模块导航：src/app/(页面路由) + src/components/(UI) + src/lib/(工具)
├─ 从现有代码提取的约定：组件函数式写法 / hooks 文件名 use-xxx / 注释用 //
├─ 联网补全的外部规范：Next.js 14 App Router 最新文档链接
├─ 真跑验证：pnpm dev 启动成功 ✅（端口 3000）
└─ 侦查来源：读了 package.json + README.md + .gitignore + git log --oneline -20
```

**生成的 AGENTS.md 片段：**
```markdown
> 继承根 {项目根}/AGENTS.md 的所有规则
> 本文件补充 src/app/ 子目录特有的约定

## src/app/ 子模块约定
- 这是 Next.js App Router 页面目录
- 每个子路由文件夹可以有 page.tsx + layout.tsx
- 服务端组件和客户端组件按 Next.js 规范写
- 动态路由：[id]/page.tsx
```

## 安装步骤

```bash
# 第 1 步：复制 agent.md
cp 10_agents-builder/agent.md ~/.trae/agents/

# 第 2 步：复制 SKILL.md（先建目录）
mkdir -p ~/.trae/skills/agents-builder
cp 10_agents-builder/SKILL.md ~/.trae/skills/agents-builder/

# 第 3 步：重启 Trae
```

## 常见问题

**Q：AGENTS.md 到底写啥？不写啥？**
A：SKILL.md 里明确边界——AGENTS.md 管"做什么"和"在哪做"（设计层），不管"怎么做"（执行层）。比如"这是 Next.js 项目，启动命令 pnpm dev"可以写（在哪做）。"代码用 2 空格缩进"这种属于执行层，从现有代码提取后也可以写（因为这是项目约定，不是通用规矩）。

**Q：嵌套规则啥意思？**
A：小项目一个根 AGENTS.md 搞定。大项目（monorepo / 多模块）根文件写总纲，子目录各自写细规则。子文件开头必须加"继承根 AGENTS.md 的所有规则"，给 AI 自动拼接上下文用——AI 进子目录时会把根文件 + 子文件一起读。

**Q：已经有 AGENTS.md 了还会建吗？**
A：不整体覆盖，只增量补充。SKILL.md 里写死了——"不许整体覆盖已有 AGENTS.md"。接手员会对比现有 AGENTS 和实际项目，缺什么补什么。

**Q：和接手员啥关系？**
A：接手员管摸底，AGENTS.md 构建员管落盘。通常接手员调完，顺手就把 AGENTS.md 建了——实际上这俩经常是一个工作流里的两个步骤。
