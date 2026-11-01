---
name: csharp-test-extension
description: 为 C# WPF 项目的 Service 类补充单元测试 + coverlet 覆盖率。Use when asked to add tests, extend test coverage, or write tests for a new Service class. 含 static 状态安全检测 + TempDbFixture 模式 + HttpListener 真实 HTTP 集成。
---

# C# Test Extension · 单元测试扩展 SOP

本项目：TeamWorkbench（net10.0-windows · WPF + Microsoft.Data.Sqlite）

## 工作区检查（开始前必扫）

```powershell
# 1. 确认测试项目存在
Test-Path TeamWorkbench.Tests/TeamWorkbench.Tests.csproj

# 2. 扫 Services/ 下哪些类已有测试覆盖
#    核心业务类（优先测）：Database / AgentRepository / GovernanceService / SeedService / EventBroadcaster
#    网络类（次优先）：HttpServer / WebSocketServer
#    进程启动类（可测但费事儿）：PowerShellRunner / HotKeyService / TrayIconService

# 3. 上一次跑的测试覆盖哪些类
dotnet test TeamWorkbench.Tests/TeamWorkbench.Tests.csproj -v minimal --no-restore
```

## static 状态安全检测（铁律！跳过必崩）

本项目有多个 static 字段：
- `Database.DbPath` — **最致命**，跨测试类并行会互相覆盖
- `EventBroadcaster._ws` — 测试不依赖 WS，设 null 安全
- `EventBroadcaster.OnEventFired` — static event，每个测试订阅必须 try-finally 取消

**规则**：
1. 必须在 `GlobalUsings.cs` 里加 `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
2. 任何调 `Database.Initialize()` 的 Service（HttpServer.StartAsync、FileInstaller 等），**无参版本必须幂等**：DbPath 非空时只跑 EnsureSchema，不覆盖
3. static event 订阅必须 try-finally 取消：
   ```csharp
   Action<string, string, object?> handler = (_, _, _) => { ... };
   EventBroadcaster.OnEventFired += handler;
   try { /* 测试 */ }
   finally { EventBroadcaster.OnEventFired -= handler; }
   ```

## TempDbFixture 模式（测试 DB 隔离）

**为什么不用 :memory:** Microsoft.Data.Sqlite 的 `Data Source=:memory:` 每条 SqliteConnection 是独立空库。AgentRepository.Insert 里 `using var conn = Database.GetConnection();` 每次 new 连接 → Seed 数据只存在 Seed 那个连接里，下一个连接什么都看不到。

**必须用临时文件**：

```csharp
public class TempDbFixture : IDisposable
{
    public readonly string TempPath;
    public TempDbFixture()
    {
        TempPath = Path.Combine(Path.GetTempPath(), $"tw_test_{Guid.NewGuid():N}.db");
        Database.Initialize(TempPath);      // 强制切路径（有参版本总是覆盖 DbPath）
        SeedService.SeedIfEmpty();          // 插 11 条 seed agent
    }
    public void Dispose()
    {
        try { if (File.Exists(TempPath)) File.Delete(TempPath); } catch { }
    }
}
```

**使用**：每个测试类 `: IDisposable` → 构造函数 `_fixture = new TempDbFixture();` → Dispose 调 `_fixture.Dispose();`

## 三类测试分层

### Layer 1 · 纯逻辑（最快，零依赖）

Models/Config 类 + 无外部依赖的纯函数。如：Agent 的 StatusLight/StatusLabel 计算、SnakeCaseJson 序列化。

### Layer 2 · DB 交互（核心价值）

调 AgentRepository / EventBroadcaster / SeedService。每个测试类自己 TempDbFixture，不共享。

### Layer 3 · 集成测试（高价值但重）

**HttpServer** 真实启动 HttpListener + HttpClient 打路由：
- 构造函数：`_server.StartAsync().GetAwaiter().GetResult();` → **必须先让 TempDbFixture 构造**（Fixture 先设 DbPath，StartAsync 里的 Initialize() 幂等跳过）
- Dispose：`_server.Stop(); _server.Dispose();` + HttpClient.Dispose()
- 用 `new HttpClient { BaseAddress = new Uri($"http://localhost:{AppConfig.HttpPort}/") }`

**测试假设**：SeedService 插的 11 条 agent 里 P0 是 01-04（4 条），P1 是 05-11（7 条）。

## 生产代码修补（写测试时顺手做）

写测试发现的真实 bug 必须当场修：

| 发现方式 | 修复 | 文件 |
|---|---|---|
| Insert 后 GetByName 返回 null | SQL 漏了 install_status/runtime_status 等列 | AgentRepository.cs |
| Insert 返回永远 0 | `DBNull.Value` 自增 + `SELECT last_insert_rowid()` | AgentRepository.cs |
| HttpServer.StartAsync 覆盖临时 DbPath | 无参 Initialize() 幂等化 | Database.cs |
| static event 订阅残留 | try-finally 取消 | 所有测试文件 |

## coverlet 跑法

```powershell
# 标准跑
dotnet test TeamWorkbench.Tests/TeamWorkbench.Tests.csproj \
  --collect:"XPlat Code Coverage"

# 输出：
# TeamWorkbench.Tests/TestResults/<GUID>/coverage.cobertura.xml

# 查行覆盖率（cobertura.xml 里 grep line-rate）
```

**不要追求 90%**。经验：WPF UI 层（MainWindow/OrbWindow/CallChainWindow）全 0% 是正常的——要测需要 UI Automation（Microsoft.VisualStudio.TestTools.UITesting），重量级。**核心业务类 60%+ 即达标**。

## Release build 冒烟

```powershell
dotnet build TeamWorkbench/TeamWorkbench.csproj -c Release --no-restore
# 必须：0 警告 0 错误
```

## git commit 分组

1. **生产代码修复**一组（Database.cs / AgentRepository.cs 等）：`fix: ...`
2. **新增测试文件**一组（TeamWorkbench.Tests/*.cs + .gitignore）：`test: +N tests → M/M 全绿`
3. **清理误提交的 bin/obj**：`.gitignore` 补全 + `git rm --cached -r TeamWorkbench.Tests/bin TeamWorkbench.Tests/obj TeamWorkbench.Tests/TestResults`

## .gitignore 必须包含

```gitignore
TeamWorkbench.Tests/bin/
TeamWorkbench.Tests/obj/
TeamWorkbench.Tests/TestResults/
```
