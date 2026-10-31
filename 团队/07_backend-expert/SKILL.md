---
name: backend-expert
description: 后端专家专属知识手册。API 设计规范 + 安全检查清单（注入/越权/敏感数据）+ 数据库约定 + Observability 最小集。
---

# 后端专家 · 专属知识手册

## 铁律原文（必引用）

铁律 #11：动手前给组合逻辑图，回「过」才动（后端改动必须过推演）
铁律 #13：安全红线绝对不能碰
铁律 #22：先推演后动手
AGENTS.md 约束：架构模式只管"做什么"，执行层细节后端专家来定

## API 设计规范

### URL + 方法

| 方法 | 语义 | 示例 |
|---|---|---|
| GET | 获取资源 | `GET /api/users` 列表 / `GET /api/users/{id}` 详情 |
| POST | 创建资源 | `POST /api/users` |
| PUT | 全量更新 | `PUT /api/users/{id}` |
| PATCH | 部分更新 | `PATCH /api/users/{id}` |
| DELETE | 删除资源 | `DELETE /api/users/{id}` |

**硬约束**：
- URL 全小写，用短横线 `/api/user-profiles`，不许驼峰
- 资源用复数 `/api/users` 不是 `/api/user`
- 动作放 query 或 request body，别放 URL `/api/users/search?q=xxx`

### 响应格式统一

```json
// 成功
{
  "code": 0,           // 0 = 成功，非 0 = 业务错误码
  "message": "ok",
  "data": { ... }      // 具体数据，列表走 data.list + data.total
}

// 错误
{
  "code": 4001,        // 业务错误码（全局唯一）
  "message": "参数非法：userId 不能为空",
  "data": null
}
```

### 分页参数

```
GET /api/users?page=1&size=20

// 响应
{
  "code": 0,
  "data": {
    "list": [...],
    "total": 150,
    "page": 1,
    "size": 20,
    "hasMore": true
  }
}
```

### 幂等性

必须标注哪些接口幂等：
- GET / PUT / DELETE = 天然幂等
- POST = 非幂等，加 `Idempotency-Key` 请求头防重复提交

## 安全检查清单（改后端必扫）

### 注入类（必查）

| 检查项 | 怎么查 | 修复方式 |
|---|---|---|
| SQL 注入 | 搜代码里的字符串拼接 SQL：`"SELECT * FROM " + tableName` | 必须参数化查询（ORM / PreparedStatement） |
| Shell 注入 | 搜 `exec()` / `system()` / `child_process.exec()` | 不许直接执行用户输入，用白名单 + 转义 |
| 命令注入 | 搜反引号 / `eval()` / 动态 import | 严禁对用户输入执行 eval |
| 模板注入 | 搜模板里直接输出 `{{ userInput }}` | 自动转义 / use safe 标记 |

### 越权类（必查）

| 检查项 | 怎么查 | 修复方式 |
|---|---|---|
| 水平越权 | 能不能通过改 userId 看别人数据？ | 加行级权限过滤（WHERE userId = 当前用户） |
| 垂直越权 | 普通用户能不能调管理员接口？ | 每个接口加角色检查中间件 |
| 未鉴权接口 | 有没有接口没加鉴权？ | 所有 `/api/*` 路由默认要鉴权，白名单显式写 |

### 敏感数据类（必查）

| 检查项 | 怎么查 | 修复方式 |
|---|---|---|
| 硬编码密码 | 搜 `password = "xxx"` / `secret = "xxx"` | 全部改环境变量 / 配置中心 |
| 密码加密 | 搜密码是存明文还是 MD5/SHA1 | 必须 bcrypt / argon2（加盐哈希） |
| 响应脱敏 | 用户信息接口返回完整手机号/身份证？ | 脱敏：`138****1234` |
| Token 泄露 | 日志里打印 token / 密码？ | 日志打之前过滤敏感字段 |

### 其他安全

| 检查项 | 要求 |
|---|---|
| 速率限制 | 登录/验证码接口必须加（防刷） |
| CORS | 生产环境不许 `Access-Control-Allow-Origin: *` |
| HTTPS | 生产必须 HTTPS，HTTP 自动跳转 |
| 错误信息 | 生产环境错误响应不许暴露堆栈和路径 |

## 数据库约定

### 表设计

- 主键统一用 `id`（BIGINT 自增 或 UUID）
- 必须有 `created_at` / `updated_at`
- 逻辑删除用 `deleted_at` 时间戳，不许物理删
- 字符集统一 `utf8mb4`（支持 emoji）
- 表名小写下划线 `user_profiles`，不许驼峰

### 索引约定

- 主键自动建
- 外键 / 唯一约束自动建
- 高频查询的 WHERE / JOIN / ORDER BY 字段建索引
- **加索引前必须先 EXPLAIN**（看执行计划，不许盲加）
- 联合索引字段顺序：等值查询字段在前，范围查询字段在后

### 事务约定

- 涉及多张表写操作 = 必须包事务
- 事务粒度尽量小（别包整个请求）
- 长事务拆分（超过 1 秒的事务要警惕）
- 隔离级别：一般用 Read Committed，特殊场景用 Serializable

### 连接池

| 参数 | 建议值 |
|---|---|
| max | CPU 核数 × 2 + 磁盘数 |
| min | 启动预热 CPU 核数 |
| idle_timeout | 30s |
| connection_timeout | 5s |

## Observability 最小集（后端必须有）

### 日志（结构化 JSON）

```json
{
  "timestamp": "2026-10-30T10:00:00Z",
  "level": "info",
  "trace_id": "abc123",
  "service": "user-service",
  "message": "用户登录成功",
  "userId": 123,
  "duration_ms": 45
}
```

### 监控（三个黄金指标）

| 指标 | 告警阈值 |
|---|---|
| 延迟 P99 | > 500ms |
| QPS | 突降 > 50% 或突增 > 200% |
| 错误率 | > 1% |

### 追踪

- 每个请求带 trace_id（从入口透传到所有下游调用）
- 关键链路能串起来：API → Service → DB → Cache
- 出问题时能用 trace_id 查到完整路径

## 输出模板：后端设计卡

```
═══ 后端设计卡 ═══
├─ 关联性：
│   ├─ 可复用（现成库/封装）：
│   └─ 依赖影响（改了影响谁）：
├─ 鉴权层（04）：
│   ├─ 认证方式：JWT / Session / OAuth
│   ├─ 权限模型：RBAC / ABAC
│   ├─ Token 过期/刷新策略：
│   └─ 密码加密：bcrypt / argon2
├─ 数据层（05）：
│   ├─ 表结构：{表名 + 字段 + 类型 + 约束}
│   ├─ 索引：{EXPLAIN 后确认的索引}
│   ├─ 事务边界：{哪些操作包在事务里}
│   └─ 迁移策略：{rollback 路径}
├─ 可观测层（06）：
│   ├─ 日志：结构化 JSON + trace_id
│   ├─ 监控：P99 / QPS / 错误率 告警阈值
│   └─ 追踪：trace_id 透传链路
├─ 接口规范：
│   ├─ 路径：POST /api/xxx
│   ├─ 请求体：{JSON Schema}
│   ├─ 响应体：{成功 + 错误码表}
│   └─ 幂等性：是/否 → 怎么保证
├─ 安全红线（已检查）：
│   ├─ ✅ 参数化查询 / ❌ 有拼接
│   ├─ ✅ 鉴权完整 / ❌ 有空洞
│   ├─ ✅ 敏感数据脱敏 / ❌ 有泄露
│   └─ ✅ 速率限制 / ❌ 没加
├─ 性能红线：
│   ├─ EXPLAIN 结果：
│   ├─ 索引覆盖：
│   ├─ 有没有 N+1：
│   └─ 大列表分页：
└─ 回滚方案：{代码 / DB 数据 / 配置 怎么回滚}
```

## 必查表

| 场景 | 必须问 |
|---|---|
| 写新接口 | 这个接口幂等吗？怎么保证？ |
| 写 SQL | 是参数化查询吗？不许拼接！ |
| 加索引 | EXPLAIN 看过了吗？ |
| 返回用户数据 | 手机号/身份证脱敏了吗？ |
| 改数据库 | 事务包了吗？回滚脚本写了吗？ |
| 加鉴权 | 所有 `/api/*` 路由默认要鉴权，白名单显式写了吗？ |
