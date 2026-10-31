---
name: project-taker
description: 接手员专属知识手册。接手 SOP 六步 + 项目画像模板 + 启动三件套 + 必查安全红线清单。
---

# 接手员 · 专属知识手册

## 铁律原文（必引用）

铁律 #22：先推演后动手（接手前必须先走接手 SOP，不许跳过 Step 3 真跑）
铁律 #25：只在聊天里说过没登记的 = 等于没发生过（接手后必须登记 REG-xx）
AGENTS.md 约束：不许整体覆盖已有 AGENTS.md，只增量补充

## 接手 SOP（六步，严格按顺序）

```
Step 1 扫目录
→ 列出顶层目录和文件
→ 识别主入口（main / index / App / server）
→ 找出构建脚本（package.json / Makefile / CMakeLists / pyproject.toml）
→ 输出：文件树骨架 + 入口文件清单

Step 2 读 AGENTS / README
→ 有没有 AGENTS.md？有就全读
→ 有没有 README.md？全读（人类文档补 AGENTS 没写的）
→ 有没有 CONTRIBUTING.md / CODE_OF_CONDUCT.md？读
→ 输出：已有约定清单 + 缺失项标记

Step 3 跑构建（能真跑就真跑，不许跳过！）
→ 真跑装依赖（npm install / pip install / go mod tidy）
→ 真跑启动（npm run dev / python main.py）
→ 真跑测试（npm test / pytest / go test）
→ 能跑通的才算"知道怎么启动"
→ 跑不通 → 记录报错 + 尝试修复 + 修复不了如实说

Step 4 查 git log
→ 最近 10 条 commit 风格
→ 谁在维护？最近一次提交时间？
→ 分支策略（main / develop / feature）
→ 有没有大量 WIP commit？（说明项目不稳定）
→ 输出：项目活跃度 + 维护状态

Step 5 写画像
→ 技术栈（语言 / 框架 / 版本）
→ 规模（文件数 / 模块数 / 依赖数）
→ 成熟度（原型 / 开发中 / 生产运行）
→ 安全红线（从 .gitignore + 代码硬编码扫描）
→ 启动三件套（装依赖 + 启动 + 测试，✅/❌）

Step 6 补 AGENTS.md + 登记台账
→ 没有 AGENTS.md → 新建（按六块区）
→ 有但不全 → 增量补充，不许整体覆盖
→ 登记 REG-xx（开工登记）
→ 输出：接手包 + AGENTS.md + REG 台账
```

## 项目画像模板

```
═══ 项目画像 ═══
├─ 项目名：
├─ 一句话描述：
├─ 技术栈：
│   ├─ 语言 / 版本：
│   ├─ 框架 / 版本：
│   ├─ 数据库 / 缓存：
│   └─ 构建工具 / 包管理器：
├─ 规模：
│   ├─ 文件数（src 目录下）：
│   ├─ 模块数：
│   ├─ 依赖数（package.json 里的）：
│   └─ 代码行数（粗略 estimate）：
├─ 成熟度：原型 / 开发中 / 生产运行
├─ 活跃度：
│   ├─ 最近一次 commit：
│   ├─ 维护者：
│   └─ commit 风格（规范 / 随意 / WIP 多）：
├─ 安全红线：
│   ├─ .gitignore 排除了什么：
│   ├─ 硬编码敏感数据扫描：✅ 干净 / ❌ 发现 x 处
│   └─ 绝对不能碰的目录/文件：
└─ 架构依赖图（文字版）：
```

## 启动三件套验证（必须真跑）

| 命令 | 预期 | 实际结果 | 状态 |
|---|---|---|---|
| 装依赖 | `npm install` / `pip install` / `go mod tidy` | | ✅ / ❌ |
| 启动开发 | `npm run dev` / `python main.py` | | ✅ / ❌ |
| 跑测试 | `npm test` / `pytest` / `go test` | | ✅ / ❌ |

三件套全 ✅ = 接手完成。有 ❌ = 必须记录报错 + 尝试修复 + 修复不了要在画像里标红。

## 必查清单

| 检查项 | 怎么查 | 发现了要做什么 |
|---|---|---|
| 有没有 AGENTS.md | `ls AGENTS.md` | 没有 → 新建；有 → 增量补充 |
| .gitignore 排除了什么 | `cat .gitignore` | 列进安全红线 |
| 有没有硬编码密码/密钥 | 搜 `password` / `secret` / `sk_` / `BEGIN RSA` | 立刻标红，建议改成环境变量 |
| .env 模板 | `ls .env*` | 有 `.env.example` → 读；没有 → 建议创建 |
| 主入口在哪 | 搜 `main` / `index` / `App` / `server` | 跟一遍调用链 |
| 能不能跑起来 | 真跑启动三件套 | 跑不通 → 记录报错 |
| git log 最近活跃吗 | `git log --oneline -10` | 超过 3 个月没 commit → 标"维护状态存疑" |

## 踩坑清单模板（启动时遇到的问题必须记录）

```
踩坑清单：
├─ 坑 1：{描述}
│   ├─ 报错信息：
│   ├─ 尝试了什么：
│   ├─ 最终解法：
│   └─ 涉及文件：
├─ 坑 2：...
└─ 还没解决的：
```
