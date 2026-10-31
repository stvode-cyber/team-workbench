## 一句话简介

接手员专岗：刚 clone 一个仓库，6 步摸家底，出项目画像 + 启动三件套 + 补 AGENTS.md。让下一个 AI 不用再走一遍摸索。

## 什么时候触发

- 刚打开一个新项目 / 刚 clone 一个仓库
- 你说"接手这个项目"、"先摸家底"、"看看这项目咋回事"
- 新项目还没有 AGENTS.md
- 跨 AI 会话要接力，需要一份开工包

## 输出长啥样（示例）

```
═══ 项目接手包 ═══
├─ 项目画像：
│   ├─ 语言/框架/版本：TypeScript + Next.js 14
│   ├─ 规模：src 下 42 个文件 / 8 个模块 / 156 个依赖
│   └─ 成熟度：开发中（main 分支有 WIP commit）
├─ 启动三件套：
│   ├─ 装依赖：pnpm install ✅ 真跑通
│   ├─ 启动：pnpm dev ✅ 端口 3000 正常
│   └─ 测试：pnpm test ❌ 2 个用例失败 → 原因：依赖版本不兼容
├─ 架构依赖图（文字版）：
│   Next.js App Router → Prisma ORM → PostgreSQL
├─ 安全红线清单：
│   .gitignore 排除了 .env / secrets/ / *.pem
├─ AGENTS.md：✅ 新建在 {项目根}/AGENTS.md
└─ 踩坑清单：
    ├─ 坑 1：装依赖时 pnpm 要求 ≥ 8.x → 解法：升级 pnpm
    └─ 坑 2：测试失败原因没定位 → 遗留，登记 GAP-01
```

## 安装步骤

```bash
# 第 1 步：复制 agent.md
cp 03_project-taker/agent.md ~/.trae/agents/

# 第 2 步：复制 SKILL.md（先建目录）
mkdir -p ~/.trae/skills/project-taker
cp 03_project-taker/SKILL.md ~/.trae/skills/project-taker/

# 第 3 步：重启 Trae
```

## 常见问题

**Q：接手员会真跑启动命令吗？**
A：会。SKILL.md 里 Step 3 是硬约束——不真跑启动三件套不算接手完成。跑不通就如实说，登记踩坑清单。

**Q：接手员和 AGENTS.md 构建员啥区别？**
A：接手员管"摸底"——扫目录、跑命令、出画像。AGENTS.md 构建员管"落盘"——按六块区把摸底结果写进文件。通常接手员调完，顺手就把 AGENTS.md 也建了。

**Q：项目已经有 AGENTS.md 了怎么办？**
A：增量补充。SKILL.md 里写了"不许整体覆盖已有 AGENTS.md，只增量补充"。接手员会对比现有 AGENTS 和实际项目，缺什么补什么。

**Q：跑启动命令需要联网/装依赖，会不会搞坏我环境？**
A：接手员的硬性边界里允许"在 Step 3 真跑时创建临时文件或改依赖（跑完清理）"。放心，真跑是必要的，不真跑接手就是瞎蒙。
