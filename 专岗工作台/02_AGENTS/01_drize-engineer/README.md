# 推演引擎 · 使用说明

## 一句话

改代码 / 花钱 / 不可逆操作前，**必须**先找我。我给你正推+反推+失败轨道+竞品五张牌，你看完再决定干不干。

## 什么时候触发

| 触发方式 | 怎么说 |
|---|---|
| 你手动调 | 「帮我推演一下这个改动」「推演一下这个方案值不值」 |
| AI 自动调 | 改 ≥3 行代码前 / git push 前 / 花钱前，Hook 可以配自动触发 |

## 输出长啥样

```
═══ 推演卡 ═══
├─ 需求：给 React 项目加 SSR
├─ 方案对比：
│   ├─ A. Next.js 迁移：风险中 / 成本高 / 效果好
│   ├─ B. Vite SSR：风险低 / 成本中 / 效果好
│   └─ C. 不迁移，加静态预渲染：风险极低 / 成本低 / 效果差
├─ 正推：选 B 的话，按 Vite SSR 文档一步步来……
├─ 反推：假设上线后崩了，根因是……
├─ 失败轨道：
│   ├─ 炸点 1：Vite SSR 文档不全 → 怎么发现 / 怎么回退
│   ├─ 炸点 2：部署环境 Node 版本不够 → …
│   └─ 炸点 3（随机）：nginx 配置和 SSR 不兼容 → …
├─ 竞品五张牌：
│   ├─ 1. SvelteKit（star 75k）做 SSR，成了
│   ├─ 2. Remix（star 30k）做 SSR，后来被 Web 标准吞并
│   ├─ ...（5 个）
│   └─ 失败案例：Next.js v13 SSR 升级事故
├─ 回滚点：每个改动都有 git revert 路径
└─ 推荐：B（Vite SSR），理由……
```

## 安装步骤

```powershell
# 1. agent.md（角色定义）→ agents 目录
Copy-Item agent.md %userprofile%\.trae-cn\agents\drize-engineer.md

# 2. SKILL.md（专属知识）→ skills 目录
New-Item -ItemType Directory %userprofile%\.trae-cn\skills\drize-engineer -Force
Copy-Item SKILL.md %userprofile%\.trae-cn\skills\drize-engineer\SKILL.md

# 3. 重启 Trae 或设置里点「重新加载 Skills」
```

## 常见问题

**Q：我只想推演个小改动，也要五张牌？**
A：五张牌是硬规矩。哪怕你觉得小，也得找齐。找不到如实说。

**Q：推演完了必须按你的推荐来吗？**
A：推演是给你参考。你可以选 A/B/C 任意一个。但风险你自己担。

**Q：能同时推演多个方案吗？**
A：可以。但每个方案必须单独跑正推+反推+失败轨道，不能合一起糊弄。
