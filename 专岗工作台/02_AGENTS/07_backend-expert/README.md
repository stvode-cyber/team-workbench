## 一句话简介

后端专家专岗：写 API / 改 DB / 加鉴权时必调，输出接口规范 + 安全审查 + 性能红线。

## 什么时候触发

- 写新 API / 改已有 API
- 设计数据库表结构 / 加索引 / 改查询
- 加鉴权 / 改认证逻辑
- 加中间件 / 改中间件
- 你说"后端"、"API"、"数据库"、"鉴权"、"接口"

## 输出长啥样（示例）

```
═══ 后端设计卡 ═══
├─ 关联性：
│   ├─ 可复用：已有 @nestjs/jwt 鉴权封装，直接用
│   └─ 依赖影响：改了 auth.ts → 所有 /api/* 路由受影响
├─ 鉴权层（04）：
│   ├─ 认证：JWT（access token + refresh token）
│   ├─ 权限：RBAC（user/admin/super）
│   └─ 密码：bcrypt（salt round 10）
├─ 数据层（05）：
│   ├─ 表结构：users(id, name, email, password_hash, role, created_at, updated_at)
│   ├─ 索引：email 唯一索引（EXPLAIN 确认 ✅）
│   ├─ 事务：创建用户 + 分配角色 包在一个事务里
│   └─ 迁移：Prisma migrate，down 脚本已写
├─ 可观测层（06）：
│   ├─ 日志：结构化 JSON + trace_id
│   ├─ 监控：P99 > 500ms 告警 / 错误率 > 1% 告警
│   └─ 追踪：trace_id 从入口透传到 DB
├─ 接口规范：
│   ├─ POST /api/auth/login
│   ├─ 请求体：{ email, password }
│   ├─ 响应体：{ code:0, data:{ accessToken, refreshToken } }
│   └─ 幂等性：否（登录发 token 每次不同），但速率限制已加
├─ 安全红线：
│   ✅ 参数化查询 / ✅ 鉴权完整 / ✅ 密码 bcrypt / ✅ 速率限制
├─ 性能红线：
│   EXPLAIN 确认 email 索引覆盖 ✅，无 N+1 ✅
└─ 回滚方案：git revert + DB 迁移 down
```

## 安装步骤

```bash
# 第 1 步：复制 agent.md
cp 07_backend-expert/agent.md ~/.trae/agents/

# 第 2 步：复制 SKILL.md（先建目录）
mkdir -p ~/.trae/skills/backend-expert
cp 07_backend-expert/SKILL.md ~/.trae/skills/backend-expert/

# 第 3 步：重启 Trae
```

## 常见问题

**Q：SQL 注入具体怎么防？**
A：SKILL.md 里明确——**不许拼接 SQL 字符串**，必须用 ORM（Prisma/TypeORM）或 PreparedStatement。搜代码里有没有 `"SELECT * FROM " + tableName` 这种就是典型注入坑。

**Q：索引是不是越多越好？**
A：不是。索引加快查询但减慢写入。加索引前必须先 EXPLAIN 看执行计划——看当前走没走全表扫描、看覆盖了没。不许盲加。

**Q：Observability 为什么要"最小集"？**
A：过度设计可观测性也是坑。最小集 = 结构化日志（带 trace_id）+ 三个黄金指标（延迟 P99 / QPS / 错误率）+ trace_id 透传链路。够定位问题就行，别搞到开天窗。

**Q：安全红线里的"水平越权"是啥？**
A：比如你是普通用户 A，请求 `/api/users/{userId}` 看自己的资料——但 userId 参数可以改成 B 的，系统没校验"只能看自己"，这就是水平越权。修复：每个接口加行级 WHERE 过滤（WHERE userId = 当前登录用户）。
