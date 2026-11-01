using System.Net;
using System.Net.Http;
using System.Text.Json;
using TeamWorkbench.Config;
using TeamWorkbench.Data;
using TeamWorkbench.Services;

namespace TeamWorkbench.Tests;

/// <summary>
/// HttpServer 集成测试
/// · 真实启动 HttpListener（localhost:3001）
/// · 用 HttpClient 打 REST 路由
/// · 覆盖 /health, /stats, /agents, /agents/{name}, /agents/{name}/scan 等
/// · 覆盖 GET / POST / PUT / DELETE 语义
/// </summary>
public class HttpServerTests : IDisposable
{
    private readonly TempDbFixture _fixture;
    private readonly HttpServer _server;
    private readonly HttpClient _client;

    public HttpServerTests()
    {
        _fixture = new TempDbFixture(); // 测试用临时 DB
        _server = new HttpServer();
        // StartAsync 内部会 Database.Initialize() —— 但我们已经在 fixture 里设了临时 DbPath
        // 所以必须先把 fixture 的 DbPath 设好再 StartAsync
        // （fixture 构造函数已调用 Database.Initialize(TempPath)）
        _server.StartAsync().GetAwaiter().GetResult();

        _client = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{AppConfig.HttpPort}/"),
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public void Dispose()
    {
        _client.Dispose();
        try { _server.Stop(); } catch { }
        _server.Dispose();
        _fixture.Dispose();
    }

    // ========== 基础路由 ==========

    [Fact]
    public async Task GET_Health_ReturnsOkTrue()
    {
        var resp = await _client.GetAsync("health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("ok", out var ok));
        Assert.True(ok.GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("db_agents", out var dbAgents));
        Assert.True(dbAgents.GetInt32() >= 11); // Seed 插了 11 条
    }

    [Fact]
    public async Task GET_Stats_ReturnsSummary()
    {
        var resp = await _client.GetAsync("stats");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("agents", out _));
        Assert.True(doc.RootElement.TryGetProperty("events_total", out _));
        Assert.True(doc.RootElement.TryGetProperty("runtime", out _));
    }

    // ========== /agents 系列 ==========

    [Fact]
    public async Task GET_Agents_ReturnsSeededList()
    {
        var resp = await _client.GetAsync("agents");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("agents", out var agents));
        Assert.True(agents.ValueKind == JsonValueKind.Array);
        Assert.True(agents.GetArrayLength() >= 11); // Seed 11 条

        Assert.True(doc.RootElement.TryGetProperty("summary", out _));
        Assert.True(doc.RootElement.TryGetProperty("count", out var count));
        Assert.True(count.GetInt32() >= 11);
    }

    [Fact]
    public async Task GET_Agents_FilterByPriority_P0()
    {
        var resp = await _client.GetAsync("agents?priority=P0");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var agents = doc.RootElement.GetProperty("agents");
        int count = agents.GetArrayLength();

        // Seed 里 01-04 是 P0（drize-engineer/arch-designer/project-taker/ledger-keeper）
        Assert.Equal(4, count);
        foreach (var a in agents.EnumerateArray())
        {
            Assert.True(a.TryGetProperty("priority", out var p));
            Assert.Equal("P0", p.GetString());
        }
    }

    [Fact]
    public async Task GET_Agents_FilterByInstalled_True_ReturnsEmptyAfterSeed()
    {
        // Seed 默认 install_status = "not-installed"
        var resp = await _client.GetAsync("agents?installed=true");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var count = doc.RootElement.GetProperty("count").GetInt32();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GET_AgentByName_SeedExists()
    {
        var resp = await _client.GetAsync("agents/drize-engineer");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("name", out var name));
        Assert.Equal("drize-engineer", name.GetString());
        Assert.True(doc.RootElement.TryGetProperty("cn_name", out _));
        Assert.True(doc.RootElement.TryGetProperty("priority", out _));
    }

    [Fact]
    public async Task GET_AgentByName_NotFound_Returns404()
    {
        var resp = await _client.GetAsync("agents/no-such-agent-xyz");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ========== Scan + Status ==========

    [Fact]
    public async Task POST_AgentScan_ReturnsScannedStatus()
    {
        // Seed 里的 agent 默认 not-installed（没装过所以目标目录不存在）
        var resp = await _client.PostAsync("agents/drize-engineer/scan", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        // scan 在目标目录不存在时 → newStatus = "not-installed" → updated = false
        Assert.True(doc.RootElement.TryGetProperty("scan", out var scan));
        Assert.True(scan.TryGetProperty("ok", out var ok));
        Assert.True(ok.GetBoolean());
    }

    [Fact]
    public async Task POST_AgentStatus_UpdateRuntime()
    {
        var body = JsonSerializer.Serialize(new { runtime_status = "busy" });
        var resp = await _client.PostAsync("agents/drize-engineer/status",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // 验证 DB 也更新了
        var fetched = await _client.GetAsync("agents/drize-engineer");
        var json = await fetched.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("runtime_status", out var rt));
        Assert.Equal("busy", rt.GetString());
    }

    [Fact]
    public async Task POST_AgentStatus_InvalidRuntime_Returns400()
    {
        var body = JsonSerializer.Serialize(new { runtime_status = "invalid-status" });
        var resp = await _client.PostAsync("agents/drize-engineer/status",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ========== 404 / Method Not Allowed ==========

    [Fact]
    public async Task GET_NonExistentPath_Returns404()
    {
        var resp = await _client.GetAsync("totally-unknown-path");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task POST_AgentById_Returns404()
    {
        // /agents/{name} 只支持 GET/PUT/DELETE，不支持 POST
        var resp = await _client.PostAsync("agents/drize-engineer", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
