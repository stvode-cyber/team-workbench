using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TeamWorkbench.Config;
using TeamWorkbench.Data;
using TeamWorkbench.Models;

namespace TeamWorkbench.Services;

/// <summary>
/// HttpListener 自托管 HTTP + WebSocket 服务器
/// 路由分发：17 条 REST（11 原始 + 6 新增）+ 1 条 WS /events
/// 异步线程池跑，不阻塞 WPF 消息泵
/// </summary>
public class HttpServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private static readonly DateTime _startTime = DateTime.Now;

    // 静态文件：启动时读一次缓存，避免每次请求都扫磁盘
    private static byte[]? _openapiJsonBytes;
    private static readonly byte[] _reDocHtmlBytes = Encoding.UTF8.GetBytes(
        @"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8""/>
<meta name=""viewport"" content=""width=device-width, initial-scale=1""/>
<title>AI 专岗员工工作台 · REST API 文档</title>
</head>
<body>
<redoc spec-url=""/openapi.json""></redoc>
<script src=""https://cdn.jsdelivr.net/npm/redoc@latest/bundles/redoc.standalone.js""></script>
</body>
</html>");

    // ============================================================
    // 生命周期
    // ============================================================

    public async Task StartAsync()
    {
        Database.Initialize();

        // 缓存 docs/openapi.json（优先从 publish 目录，其次从 project root）
        try
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "docs", "openapi.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "docs", "openapi.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "docs", "openapi.json"),
            };
            foreach (var p in candidates)
            {
                if (File.Exists(p)) { _openapiJsonBytes = File.ReadAllBytes(p); break; }
            }
            if (_openapiJsonBytes == null)
                Debug.WriteLine("⚠️ openapi.json not found in known locations (skip /openapi.json endpoint)");
        }
        catch (Exception ex) { Debug.WriteLine($"⚠️ openapi.json cache failed: {ex.Message}"); }
        _listener = new HttpListener();
        _listener.Prefixes.Add(AppConfig.LocalhostPrefix);
        _listener.Start();
        _cts = new CancellationTokenSource();

        Debug.WriteLine($"🚀 HttpServer started on {AppConfig.LocalhostPrefix}");

        _listenTask = LoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
    }

    public void Dispose() => Stop();

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleContextAsync(ctx, ct);
            }
            catch (HttpListenerException) { break; }
        }
    }

    // ============================================================
    // 路由分发
    // ============================================================

    private async Task HandleContextAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        try
        {
            var path = req.Url?.AbsolutePath ?? "/";
            var method = req.HttpMethod.ToUpperInvariant();
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // WebSocket 端点
            if (path == "/events")
            {
                if (req.IsWebSocketRequest) { await WebSocketServer.HandleAsync(ctx); return; }
                await WriteJsonAsync(resp, 400, new { error = "Expected WebSocket" }); return;
            }

            // ========== 静态文件：/openapi.json + /docs（ReDoc） ==========
            if (segments.Length == 1 && segments[0] == "openapi.json" && method == "GET")
            {
                await HandleOpenApi(resp); return;
            }
            if (segments.Length == 1 && segments[0] == "docs" && method == "GET")
            {
                await HandleDocs(resp); return;
            }

            // ========== P1 新增：/health, /stats, /events ==========
            if (segments.Length == 1 && segments[0] == "health" && method == "GET")
            {
                await HandleHealth(resp); return;
            }
            if (segments.Length == 1 && segments[0] == "stats" && method == "GET")
            {
                await HandleStats(resp); return;
            }
            if (segments.Length == 1 && segments[0] == "events")
            {
                if (method != "GET") { await WriteJsonAsync(resp, 405, new { error = "Method Not Allowed" }); return; }
                await HandleEvents(req, resp); return;
            }
            if (segments.Length == 2 && segments[0] == "events" && segments[1] == "stats" && method == "GET")
            {
                await HandleEventsStats(resp); return;
            }

            // ========== /agents 系列 ==========
            if (segments.Length == 1 && segments[0] == "agents")
            {
                if (method == "GET") { await HandleGetAll(req, resp); return; }
                if (method == "POST") { await HandleCreateAgent(req, resp); return; }
            }

            if (segments.Length == 2 && segments[0] == "agents")
            {
                var name = segments[1];
                if (method == "GET") { await HandleGetByName(resp, name); return; }
                if (method == "PUT") { await HandleUpdateAgent(req, resp, name); return; }
                if (method == "DELETE") { await HandleDeleteAgent(resp, name); return; }
            }

            if (segments.Length == 3 && segments[0] == "agents")
            {
                var name = segments[1];
                var action = segments[2];
                switch (action)
                {
                    case "scan" when method == "POST":
                        await HandleScan(name, resp); return;
                    case "status" when method == "POST":
                        await HandleStatus(req, name, resp); return;
                    case "events" when method == "GET":
                        await HandleGetEvents(name, resp); return;
                    case "depends" when method == "POST":
                        await HandleDepends(req, name, resp); return;
                    case "install" when method == "POST":
                        await HandleInstall(name, resp); return;
                    case "uninstall" when method == "POST":
                        await HandleUninstall(name, resp); return;
                }
            }

            if (segments.Length == 2 && segments[0] == "agents" && segments[1] == "scan-all" && method == "POST")
            {
                await HandleScanAll(resp); return;
            }
            if (segments.Length == 2 && segments[0] == "agents" && segments[1] == "install-all" && method == "POST")
            {
                await HandleInstallAll(resp); return;
            }

            await WriteJsonAsync(resp, 404, new { error = "Not Found", path, method });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ HttpServer error: {ex.Message}");
            await WriteJsonAsync(resp, 500, new { error = ex.Message });
        }
        finally { try { resp.Close(); } catch { } }
    }

    // ============================================================
    // 原有 11 条 handler
    // ============================================================

    // 改：加 query 筛选 ?priority=P0&installed=true&runtime=busy
    private static async Task HandleGetAll(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var agents = AgentRepository.GetAll();

        // P1: query 筛选（内存过滤，11 条数据足够快）
        var qs = req.QueryString;
        string? qPriority = qs["priority"]?.ToUpperInvariant();
        string? qInstalled = qs["installed"]?.ToLowerInvariant();
        string? qRuntime = qs["runtime"]?.ToLowerInvariant();

        if (!string.IsNullOrEmpty(qPriority) && (qPriority == "P0" || qPriority == "P1"))
            agents = agents.Where(a => a.Priority == qPriority).ToList();
        if (!string.IsNullOrEmpty(qInstalled) && (qInstalled == "true" || qInstalled == "false"))
            agents = agents.Where(a => (qInstalled == "true") == (a.InstallStatus == "installed")).ToList();
        if (!string.IsNullOrEmpty(qRuntime) && (qRuntime == "busy" || qRuntime == "idle"))
            agents = agents.Where(a => a.RuntimeStatus == qRuntime).ToList();

        var summary = AgentRepository.GetSummary();
        await WriteJsonAsync(resp, 200, new { agents, summary, count = agents.Count });
    }

    private static async Task HandleGetByName(HttpListenerResponse resp, string name)
    {
        var agent = AgentRepository.GetByName(name);
        if (agent == null) { await WriteJsonAsync(resp, 404, new { error = $"agent '{name}' not found" }); return; }
        await WriteJsonAsync(resp, 200, agent);
    }

    // P0 Bug fix: ScanAll → ScanOne（之前扫全量 11 条再返回单条）
    private static async Task HandleScan(string name, HttpListenerResponse resp)
    {
        var agent = AgentRepository.GetByName(name);
        if (agent == null) { await WriteJsonAsync(resp, 404, new { error = $"agent '{name}' not found" }); return; }
        var scan = FileInstaller.ScanOne(name);
        var refreshed = AgentRepository.GetByName(name);
        await WriteJsonAsync(resp, 200, new { agent = refreshed, scan });
    }

    private static async Task HandleStatus(HttpListenerRequest req, string name, HttpListenerResponse resp)
    {
        var agent = AgentRepository.GetByName(name);
        if (agent == null) { await WriteJsonAsync(resp, 404, new { error = $"agent '{name}' not found" }); return; }

        var body = await ReadBodyAsync(req);
        string newStatus = "idle";
        try
        {
            var doc = JsonDocument.Parse(body);
            newStatus = doc.RootElement.TryGetProperty("runtime_status", out var v) ? v.GetString() ?? "idle" : "idle";
        }
        catch { }

        if (newStatus != "idle" && newStatus != "busy")
        {
            await WriteJsonAsync(resp, 400, new { error = "runtime_status must be 'idle' or 'busy'" }); return;
        }

        AgentRepository.UpdateRuntimeStatus(name, newStatus);
        EventBroadcaster.Broadcast("runtime", name, new { new_status = newStatus });
        await WriteJsonAsync(resp, 200, new { ok = true, runtime_status = newStatus });
    }

    private static async Task HandleGetEvents(string name, HttpListenerResponse resp)
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM status_events WHERE agent_name = $name ORDER BY id DESC LIMIT 50";
        cmd.Parameters.AddWithValue("$name", name);
        var list = ReadEvents(cmd);
        await WriteJsonAsync(resp, 200, list);
    }

    private static async Task HandleInstall(string name, HttpListenerResponse resp)
    {
        var r = FileInstaller.Install(name);
        if (!r.Ok) { await WriteJsonAsync(resp, 400, new { ok = false, errors = r.Errors }); return; }
        await WriteJsonAsync(resp, 200, r);
    }

    private static async Task HandleUninstall(string name, HttpListenerResponse resp)
    {
        var r = FileInstaller.Uninstall(name);
        if (!r.Ok) { await WriteJsonAsync(resp, 400, new { ok = false, errors = r.Errors }); return; }
        await WriteJsonAsync(resp, 200, r);
    }

    private static async Task HandleScanAll(HttpListenerResponse resp)
    {
        var r = FileInstaller.ScanAll();
        await WriteJsonAsync(resp, 200, r);
    }

    private static async Task HandleInstallAll(HttpListenerResponse resp)
    {
        var r = FileInstaller.InstallAll();
        await WriteJsonAsync(resp, 200, r);
    }

    private static async Task HandleCreateAgent(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var body = await ReadBodyAsync(req);
        Agent? candidate;
        try { candidate = JsonSerializer.Deserialize<Agent>(body, JsonOpts); }
        catch { await WriteJsonAsync(resp, 400, new { error = "Invalid JSON body" }); return; }
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.Name))
        {
            await WriteJsonAsync(resp, 400, new { error = "name is required" }); return;
        }

        var gateResult = GovernanceService.RunGateChecks(candidate);
        if (!gateResult.AllPass) { await WriteJsonAsync(resp, 422, gateResult); return; }

        try
        {
            AgentRepository.Insert(candidate);
            EventBroadcaster.Broadcast("create", candidate.Name, new { cn_name = candidate.CnName });
            await WriteJsonAsync(resp, 201, candidate);
        }
        catch (Exception ex) { await WriteJsonAsync(resp, 400, new { error = ex.Message }); }
    }

    private static async Task HandleUpdateAgent(HttpListenerRequest req, HttpListenerResponse resp, string name)
    {
        var existing = AgentRepository.GetByName(name);
        if (existing == null) { await WriteJsonAsync(resp, 404, new { error = $"agent '{name}' not found" }); return; }

        var body = await ReadBodyAsync(req);
        Agent? updated;
        try { updated = JsonSerializer.Deserialize<Agent>(body, JsonOpts); }
        catch { await WriteJsonAsync(resp, 400, new { error = "Invalid JSON" }); return; }
        if (updated == null) { await WriteJsonAsync(resp, 400, new { error = "Invalid body" }); return; }

        updated.Name = name;
        AgentRepository.Update(updated);
        await WriteJsonAsync(resp, 200, updated);
    }

    private static async Task HandleDeleteAgent(HttpListenerResponse resp, string name)
    {
        var existing = AgentRepository.GetByName(name);
        if (existing == null) { await WriteJsonAsync(resp, 404, new { error = $"agent '{name}' not found" }); return; }

        AgentRepository.Delete(name);
        EventBroadcaster.Broadcast("delete", name);
        await WriteJsonAsync(resp, 200, new { ok = true, name });
    }

    // ============================================================
    // 静态文件 handler：/openapi.json + /docs（ReDoc）
    // ============================================================

    private static async Task HandleOpenApi(HttpListenerResponse resp)
    {
        if (_openapiJsonBytes == null)
        {
            await WriteJsonAsync(resp, 404, new { error = "openapi.json not found" }); return;
        }
        resp.StatusCode = 200;
        resp.ContentType = "application/json; charset=utf-8";
        resp.ContentLength64 = _openapiJsonBytes.Length;
        await resp.OutputStream.WriteAsync(_openapiJsonBytes, 0, _openapiJsonBytes.Length);
        await resp.OutputStream.FlushAsync();
    }

    private static async Task HandleDocs(HttpListenerResponse resp)
    {
        resp.StatusCode = 200;
        resp.ContentType = "text/html; charset=utf-8";
        resp.ContentLength64 = _reDocHtmlBytes.Length;
        await resp.OutputStream.WriteAsync(_reDocHtmlBytes, 0, _reDocHtmlBytes.Length);
        await resp.OutputStream.FlushAsync();
    }

    // ============================================================
    // P1 新增 handler：health / stats / events / events-stats / depends
    // ============================================================

    private static async Task HandleHealth(HttpListenerResponse resp)
    {
        var agents = AgentRepository.GetAll();
        await WriteJsonAsync(resp, 200, new
        {
            ok = true,
            version = "v3.0-websocket-live",
            db_agents = agents.Count,
            uptime_sec = (int)(DateTime.Now - _startTime).TotalSeconds
        });
    }

    private static async Task HandleStats(HttpListenerResponse resp)
    {
        var summary = AgentRepository.GetSummary();

        using var conn = Database.GetConnection();
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM status_events";
        int eventsTotal = Convert.ToInt32(countCmd.ExecuteScalar());

        using var uptimeCmd = conn.CreateCommand();
        uptimeCmd.CommandText = "SELECT version, updated_at FROM schema_version ORDER BY updated_at DESC LIMIT 1";
        string? dbVersion = null, dbUpdatedAt = null;
        using (var r = uptimeCmd.ExecuteReader())
        {
            if (r.Read()) { dbVersion = r.GetString(0); dbUpdatedAt = r.IsDBNull(1) ? null : r.GetString(1); }
        }

        await WriteJsonAsync(resp, 200, new
        {
            agents = new
            {
                summary.Total, summary.Installed, summary.Partial, summary.Missing,
                summary.Busy, idle = summary.Total - summary.Busy
            },
            events_total = eventsTotal,
            runtime = new
            {
                port = AppConfig.HttpPort,
                uptime_sec = (int)(DateTime.Now - _startTime).TotalSeconds,
                db_schema_version = dbVersion,
                db_updated_at = dbUpdatedAt
            }
        });
    }

    private static async Task HandleEvents(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var qs = req.QueryString;
        string? type = qs["type"];
        string? limitStr = qs["limit"];
        int limit = int.TryParse(limitStr, out var l) ? Math.Clamp(l, 1, 500) : 100;

        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        if (!string.IsNullOrEmpty(type))
        {
            cmd.CommandText = "SELECT * FROM status_events WHERE event_type = $type ORDER BY id DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$type", type);
        }
        else
        {
            cmd.CommandText = "SELECT * FROM status_events ORDER BY id DESC LIMIT $limit";
        }
        cmd.Parameters.AddWithValue("$limit", limit);
        var list = ReadEvents(cmd);
        await WriteJsonAsync(resp, 200, new { count = list.Count, events = list });
    }

    private static async Task HandleEventsStats(HttpListenerResponse resp)
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT event_type, COUNT(*) as cnt FROM status_events
            GROUP BY event_type ORDER BY cnt DESC
            """;
        var breakdown = new List<object>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                breakdown.Add(new { event_type = r.GetString(0), count = r.GetInt32(1) });
        }

        using var totalCmd = conn.CreateCommand();
        totalCmd.CommandText = "SELECT COUNT(*) FROM status_events";
        int total = Convert.ToInt32(totalCmd.ExecuteScalar());

        await WriteJsonAsync(resp, 200, new { total, breakdown });
    }

    private static async Task HandleDepends(HttpListenerRequest req, string name, HttpListenerResponse resp)
    {
        var existing = AgentRepository.GetByName(name);
        if (existing == null) { await WriteJsonAsync(resp, 404, new { error = $"agent '{name}' not found" }); return; }

        var body = await ReadBodyAsync(req);
        List<string>? depends = null;
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("depends_on", out var arr))
            {
                depends = arr.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }
        }
        catch { await WriteJsonAsync(resp, 400, new { error = "Invalid JSON" }); return; }

        depends ??= new List<string>();

        // 验证：所有 depends_on 里的 agent 必须存在（软检查，不硬拦）
        var missing = depends.Where(d => AgentRepository.GetByName(d) == null).ToList();

        existing.DependsOn = depends;
        AgentRepository.Update(existing);
        EventBroadcaster.Broadcast("depends", name, new { depends_on = depends });

        await WriteJsonAsync(resp, 200, new
        {
            ok = true,
            agent_name = name,
            depends_on = depends,
            missing_agents = missing
        });
    }

    // ============================================================
    // 工具方法
    // ============================================================

    private static List<object> ReadEvents(System.Data.Common.DbCommand cmd)
    {
        var list = new List<object>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new
            {
                id = reader.GetInt32(0),
                agent_name = reader.GetString(1),
                event_type = reader.GetString(2),
                event_data = reader.IsDBNull(3) ? null : reader.GetString(3),
                created_at = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }
        return list;
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest req)
    {
        using var stream = req.InputStream;
        using var reader = new StreamReader(stream, req.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private static async Task WriteJsonAsync(HttpListenerResponse resp, int statusCode, object data)
    {
        resp.StatusCode = statusCode;
        resp.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(data, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        await resp.OutputStream.FlushAsync();
    }
}
