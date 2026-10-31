# 开源 MCP Server 精选

> 生成时间：2026-10-30 · 来源：modelcontextprotocol/servers（87,500 star）+ punkpeye/awesome-mcp-servers（89,000 star）

## P0 必装（装一次永久生效）

| # | MCP | star | 仓库 | 启动命令 | 环境变量 |
|---|---|---|---|---|---|
| 1 | GitHub MCP | 30,800+ | github.com/github/github-mcp-server | Trae 里选 GitHub | `GITHUB_TOKEN=ghp_xxx` |
| 2 | Playwright MCP | 34,100+ | github.com/microsoft/playwright-mcp | `npx @playwright/mcp@latest` | - |
| 3 | SQLite MCP | 官方 | modelcontextprotocol/servers | `npx @modelcontextprotocol/server-sqlite /path/to/db.db` | - |

## P1 推荐

| # | MCP | star | 场景 |
|---|---|---|---|
| 4 | Windows Filesystem MCP | 社区 | 直接操作 Windows 文件系统 |
| 5 | Figma MCP | 官方 | 读 Figma 设计稿，需要 `FIGMA_ACCESS_TOKEN` |
| 6 | Notion MCP | 社区 | 读写 Notion 文档，需要 `NOTION_TOKEN` |
| 7 | npm MCP | 社区 | 查 npm 包版本/star/漏洞 |

## 通用安装步骤

Trae 设置 → MCP Server → 添加 → 填：
1. 名称（随便起）
2. 启动命令（见上表）
3. 环境变量（如果需要 token）
4. 保存 → 下次对话自动生效

## Windows 前置

- 需要 Node.js v20+（nodejs.org 下载 LTS）
- PowerShell 里 `npx` 写成 `npx.cmd`（避免脚本执行策略限制）
