---
name: safety-auditor
description: 安全巡视员专属知识手册。版本对齐铁律 + 硬编码扫描清单 + 别碰文件检查 + npm audit 解读 + 发版前 5 检。
---

# 安全巡视员 · 专属知识手册

## 铁律原文（必引用）

铁律 #13：安全红线绝对不能碰（别碰文件清单来自 .gitignore + AGENTS.md）
铁律 #29：版本号方向绝对不能反（version-iron-law）
铁律 #33：不许编造（没扫到说扫到了 = 谎报）

## 版本对齐铁律（version-iron-law）

### 检查点

```
Step 1 读版本号
→ 读 package.json / pubspec.yaml / Cargo.toml 等版本文件
→ 确认版本号递增方向正确（新版本 > 旧版本）
→ 语义化版本：主版本（破坏性变更）/ 次版本（新功能）/ 补丁（修 bug）

Step 2 CHANGELOG 对齐
→ CHANGELOG.md 里有没有记录这个版本的改动？
→ release notes 和版本号对得上吗？
→ 版本号标了 v1.2.3 但 CHANGELOG 只写到 v1.2.0 → 标红

Step 3 多包版本一致性
→ monorepo 特别重要
→ 多个子包版本号必须一致（或在允许的范围内）
→ 搜所有 package.json 看 version 字段

Step 4 版本比较方向
→ 强制更新弹窗里的版本比较：新版本必须返回"需要更新"
→ 禁止：比较方向反了 → 用户装了新包但还弹"需要更新"
```

### 常见坑

| 坑 | 怎么查 |
|---|---|
| 版本号写反 | 用 `semver.compare()` 验证旧 < 新 |
| CHANGELOG 没更新 | 对比 CHANGELOG 最后一条和 package.json 版本 |
| 多包版本不一致 | grep -r '"version":' 所有 package.json |
| 构建产物版本没更新 | 检查 dist/ 目录里的产物版本号 |

## 硬编码敏感数据扫描清单

### 必扫模式

| 模式 | 正则/关键词 | 示例 |
|---|---|---|
| 密码 | `password\s*=\s*['"].+['"]` | `password = "admin123"` |
| 密钥 | `secret\s*=\s*['"].+['"]` | `secret = "sk_live_xxx"` |
| 私钥 | `BEGIN (RSA|EC|OPENSSH) PRIVATE KEY` | 私钥文件内容 |
| Token | `token\s*=\s*['"].+['"]` | `token = "ghp_xxx"` |
| API Key | `api[_-]?key\s*=\s*['"].+['"]` | `apiKey = "AIza..."` |
| 连接字符串 | `(mysql|postgres|mongodb)://.+:+.+@.+` | `mysql://root:pass@host/db` |
| AWS Key | `AKIA[0-9A-Z]{16}` | AWS Access Key ID |
| GitHub Token | `gh[pousr]_[A-Za-z0-9_]{36,}` | GitHub Personal Access Token |

### 区分真数据 vs 假数据

- 真数据：能跑通生产 / 长度 ≥ 20 字符 / 不是测试 fixture 里的
- 假数据：测试用 fixture 里的 / 长度短 / 带 "test" / "mock" / "example"
- 假数据也要列出来（方便区分），但标"测试用，风险低"

## 别碰文件检查

### 来源

```
别碰文件清单 = .gitignore 排除的 + AGENTS.md 里安全红线列的 + 项目约定的敏感路径
```

### 必查项

| 来源 | 怎么查 | 示例 |
|---|---|---|
| .gitignore | 读 .gitignore 内容 | `*.pem`、`.env`、`secrets/` |
| AGENTS.md 安全红线 | 读 AGENTS.md 里的安全红线块 | "绝对不能改的配置目录：`config/production/`" |
| 硬编码配置 | 搜 `config.prod` / `prod.yml` / `prod.json` | 生产数据库配置 |
| 证书密钥 | 搜 `*.pem` / `*.key` / `*.crt` | SSL 证书 |

### 改动触碰了怎么办

- 触碰了别碰文件 → 立刻标红
- 说明为什么触碰（是新增？是误改？）
- 如果是必须改（比如证书轮换），走正规流程：
  - 登记 IMP-xx（影响圈）
  - 登记 REG-xx（开工登记）
  - 改完后立刻进安全巡视员再扫一遍

## npm audit 解读

### 命令

```bash
npm audit          # 只报告
npm audit fix      # 自动修复（小版本）
npm audit fix --force  # 强制修复（可能大版本，谨慎！）
```

### 严重级别

| 级别 | 含义 | 处理方式 |
|---|---|---|
| Critical | 远程代码执行 / 直接泄露敏感数据 | 立刻修，必须升级到安全版本 |
| High | 可以拿到用户数据 / 绕过鉴权 | 24 小时内修 |
| Moderate | DoS / 有限的数据泄露 | 排期修 |
| Low | 信息泄露（需要用户交互） | 有空再修 |

### 解读原则

- 不许只看数字（"3 个漏洞"），要看具体 CVE 和修复版本
- 不许盲目 `npm audit fix --force`（可能大版本 Breaking Change）
- 修完必须跑测试验证没破坏功能

## 注入风险扫描

| 类型 | 搜什么 | 修什么 |
|---|---|---|
| SQL 拼接 | `+ "SELECT` / `+ "INSERT` / 模板字符串里的 SQL | 改成 ORM / 参数化查询 |
| Shell 注入 | `exec(` / `system(` / `ProcessBuilder(` | 白名单 + 参数化 |
| 命令注入 | `eval(` / `new Function(` | 严禁执行用户输入 |
| HTML 拼接 | 直接输出用户输入到 HTML | 转义 / 用安全模板 |

## 发版前 5 检

```
1. 构建能通过吗？
   → 真跑 npm run build / 能真过才叫通过
   → 构建时间超过 5 分钟要警惕

2. 环境变量有没有从本地带到生产？
   → 搜 localhost / 127.0.0.1 / 本地测试数据
   → .env.production 和 .env.local 分开

3. 数据库迁移脚本有没有回滚路径？
   → 每个 migration 文件能不能 down？
   → 回滚会丢数据吗？

4. 发版说明写了没？
   → CHANGELOG / release notes
   → 必须包含：新功能 / 修了什么 bug / 破坏性变更 / 已知问题

5. 所有安全检查都过了吗？
   → 版本对齐 ✅
   → 别碰文件 ✅
   → 硬编码扫描 ✅
   → 注入风险 ✅
   → npm audit ✅
```

## 输出模板：安全巡检卡

```
═══ 安全巡检卡 ═══
├─ 巡检对象：{项目名 / 版本号}
├─ 巡检时点：提交前 / 发版前 / PR review
│
├─ ① 版本对齐：
│   ├─ 当前版本：xxx → 目标版本：xxx
│   ├─ 递增方向：✅ 正确 / ❌ 方向反了
│   ├─ CHANGELOG 对齐：✅ 对得上 / ❌ 落后
│   └─ 多包一致性（monorepo）：✅ / ❌ 哪个不一致
│
├─ ② 别碰文件检查：
│   ├─ .gitignore 排除清单：{列表}
│   ├─ AGENTS.md 安全红线：{列表}
│   ├─ 本次改动触碰清单：✅ 无 / ❌ 触碰了 {文件}
│   └─ 触碰原因：{说明}
│
├─ ③ 硬编码扫描：
│   ├─ ✅ 干净 / ❌ 发现 {x} 处
│   ├─ 位置 1（文件:行号）：{代码片段} → 类型：密码/密钥/token
│   ├─ 位置 2：...
│   └─ 测试用假数据：{列表，标风险低}
│
├─ ④ 注入风险：
│   ├─ SQL 拼接：✅ 无 / ❌ 发现 {x} 处
│   ├─ Shell 注入：✅ / ❌
│   ├─ 命令注入（eval）：✅ / ❌
│   └─ HTML 拼接（XSS）：✅ / ❌
│
├─ ⑤ 发版前额外：
│   ├─ 构建：✅ 通过（耗时 xx s）/ ❌ 报错
│   ├─ 环境变量：✅ 干净 / ❌ 发现 {本地数据}
│   ├─ DB 迁移回滚：✅ 有路径 / ❌ 没写
│   ├─ npm audit：✅ 0 Critical / ❌ {x} Critical / {y} High
│   └─ 发版说明：✅ 写了 / ❌ 没写
│
└─ 严重级别汇总：
    ├─ 🔴 Critical（必须修才能发）：{列表，没写就是 0}
    ├─ 🟡 Warning（建议修）：{列表}
    └─ 🟢 OK（可以继续）：全部检查项 ✅
```

## 必查表

| 场景 | 必须问 |
|---|---|
| 发版前 | 版本号递增方向对吗？ |
| 提 PR | 别碰文件清单读过了吗？改动触碰了吗？ |
| 改配置 | 这是生产密钥吗？是不是应该走环境变量？ |
| 合代码 | npm audit 过了吗？有 Critical 吗？ |
| 构建 | 本地能 build 通过吗？（不是"应该能"，是真跑过） |
