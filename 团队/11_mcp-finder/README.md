## 一句话简介

MCP 搜索员专岗：需要新能力但不确定有没有现成 MCP 时，4 来源全查，star≥20 才推荐，找不到就说找不到。

## 什么时候触发

- 你说"有没有 MCP"、"找个 MCP"、"装个 MCP"、"需要 XX 能力"
- 某个 Subagent 需要工具但现有 MCP 覆盖不到
- 不确定某个能力能不能用 MCP 实现
- 你主动说"帮我搜搜 MCP"

## 输出长啥样（示例）

```
═══ MCP 搜索结果 ═══
├─ 需求：需要能操作 GitHub issue 的 MCP
├─ 搜索范围：
│   ├─ ✅ 官方 awesome（modelcontextprotocol/servers + punkpeye/awesome-mcp-servers）
│   ├─ ✅ Trae 社区
│   ├─ ✅ npm/pypi
│   └─ ✅ GitHub 关键词
│
├─ ✅ 找到可用的：
│   ├─ MCP 名称：@modelcontextprotocol/server-github（官方维护）
│   ├─ 仓库：https://github.com/modelcontextprotocol/servers（star 12.5k，最近 commit 2 周前）
│   ├─ 能提供的工具：create_issue / list_issues / get_issue / update_issue / comment
│   ├─ 启动命令：npx @modelcontextprotocol/server-github
│   ├─ 需要环境：node >= 18
│   ├─ 需要 token：是（GitHub Personal Access Token）
│   ├─ 匹配度：⭐⭐⭐⭐⭐ 完美适配
│   └─ Trae 用户反馈：论坛多人分享跑通过 ✅
│
├─ ⚠️ 有但不够好：
│   └─ 社区版 github-mcp（star 18，README 不全）— 不推荐，用官方版
│
├─ 相关推荐：可能还需要 mcp-git（操作 git 仓库）
└─ 最终建议：装 @modelcontextprotocol/server-github
    └─ 理由：官方维护、star 最高、Trae 社区验证过

【Trae 里装的步骤】
1. 设置 → MCP Server → 添加
2. 名称：github-mcp（随便起）
3. 启动命令：npx @modelcontextprotocol/server-github
4. 环境变量：GITHUB_TOKEN=ghp_xxx（从 GitHub → Settings → Developer settings → Personal access tokens 申请）
5. 保存，下次对话自动有这个能力
```

## 安装步骤

```bash
# 第 1 步：复制 agent.md
cp 11_mcp-finder/agent.md ~/.trae/agents/

# 第 2 步：复制 SKILL.md（先建目录）
mkdir -p ~/.trae/skills/mcp-finder
cp 11_mcp-finder/SKILL.md ~/.trae/skills/mcp-finder/

# 第 3 步：重启 Trae
```

## 常见问题

**Q：找不到 MCP 会编造吗？**
A：绝对不会。SKILL.md 第一条铁律——"找不到就说找不到，不许瞎编"。编造一个假 MCP 比完全没有 MCP 更糟糕（你会信以为真去装，浪费时间）。找不到会给替代方案：用 Trae 内置工具能不能间接实现 + 自建难度评估。

**Q：star ≥ 20 是硬线吗？为什么？**
A：是。star < 20 说明基本没人用，大概率是死仓库或半吊子。再加上"最近 3 个月有 commit"这一条，过滤掉不维护的。两条都满足才进推荐。

**Q：官方 awesome-mcp 和社区 awesome 啥区别？**
A：官方仓库（modelcontextprotocol/servers）里的 MCP 是官方写的参考实现，star 最多、最稳。社区 awesome（punkpeye/awesome-mcp-servers）是用户维护的全量清单，分类更细但鱼龙混杂。两个都要查——官方覆盖主流，社区覆盖长尾。

**Q：装 MCP 时 token 怎么填？安全吗？**
A：Trae 设置里 MCP Server 有"环境变量"字段——在那填 `GITHUB_TOKEN=ghp_xxx`，不要把 token 写在启动命令里（比如 `npx xxx --token ghp_xxx`）。Trae 会加密存储，不会明文出现在对话日志里。也绝对不要提交到 Git。

**Q：4 个来源都要查吗？还是随便挑？**
A：SKILL.md 里写"至少查 2 个，推荐全查"。官方 awesome + GitHub 关键词是必查的（覆盖最广）。Trae 社区和 npm/pip 补漏。
