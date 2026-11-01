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
/// 路由分发：11 条 REST + 1 条 WS /events
/// 异步线程池跑，不阻塞 WPF 消息泵
/// </summary>
public class HttpServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    // ============================================================
    // 生命周期
    // ============================================================

    public async Task StartAsync()
    {
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
                // fire-and-forget 每个请求（不阻塞下一个请求）
                _ = HandleContextAsync(ctx, ct);
            }
            catch (HttpListenerException)
            {
                // Stop 时正常抛
                break;
            }
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

            // WebSocket 端点
            if (path == "/events")
            {
                if (req.IsWebSocketRequest)
                {
                    await WebSocketServer.HandleAsync(ctx);
                    return;
                }
                await WriteJsonAsync(resp, 400, new { error = "Expected WebSocket" });
                return;
            }

            // 11 条 REST 路由 — 精确匹配优先，模板匹配其次
            // GET  /agents                → 全量
            // GET  /agents/{name}         → 单条
            // POST /agents/{name}/scan    → 扫安装状态
            // POST /agents/{name}/status  → 改 runtime_status
            // GET  /agents/{name}/events  → 查事件
            // POST /agents/{name}/install → 安装
            // POST /agents/{name}/uninstall → 卸载
            // POST /agents/scan-all       → 全量扫描
            // POST /agents/install-all    → 全量安装
            // POST /agents                → 创建新 agent + 5 门槛
            // PUT  /agents/{name}         → 更新
            // DELETE /agents/{name}       → 删除

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 1 && segments[0] == "agents")
            {
                if (method == "GET") { await HandleGetAll(resp); return; }
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
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    // ============================================================
    // Handler 实现
    // ============================================================

    private static async Task HandleGetAll(HttpListenerResponse resp)
    {
        var agents = AgentRepository.GetAll();
        var summary = AgentRepository.GetSummary();
        await WriteJsonAsync(resp, 200, new { agents, summary });
    }

    private static async Task HandleGetByName(HttpListenerResponse resp, string name)
    {
        var agent = AgentRepository.GetByName(name);
        if (agent == null) { await WriteJsonAsync(resp, 404, new { error = $"agent '{name}' not found" }); return; }
        await WriteJsonAsync(resp, 200, agent);
    }

    private static async Task HandleScan(string name, HttpListenerResponse resp)
    {
        var agent = AgentRepository.GetByName(name);
        if (agent == null) { await WriteJsonAsync(resp, 404, new { error = $"agent '{name}' not found" }); return; }
        var result = FileInstaller.ScanAll(); // 全量扫，拿到所有后返回这个 agent 的状态
        var refreshed = AgentRepository.GetByName(name);
        await WriteJsonAsync(resp, 200, new { agent = refreshed, scan_all = result });
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
        try
        {
            candidate = JsonSerializer.Deserialize<Agent>(body, JsonOpts);
        }
        catch
        {
            await WriteJsonAsync(resp, 400, new { error = "Invalid JSON body" }); return;
        }
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.Name))
        {
            await WriteJsonAsync(resp, 400, new { error = "name is required" }); return;
        }

        // 5 条治理门槛
        var gateResult = GovernanceService.RunGateChecks(candidate);
        if (!gateResult.AllPass)
        {
            await WriteJsonAsync(resp, 422, gateResult); return;
        }

        try
        {
            AgentRepository.Insert(candidate);
            EventBroadcaster.Broadcast("create", candidate.Name, new { cn_name = candidate.CnName });
            await WriteJsonAsync(resp, 201, candidate);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(resp, 400, new { error = ex.Message });
        }
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

        // 保持 name 不变
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
    // 工具方法
    // ============================================================

    private static async Task<string> ReadBodyAsync(HttpListenerRequest req)
    {
        using var stream = req.InputStream;
        using var reader = new StreamReader(stream, req.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    // 统一 JSON options — snake_case（和 Node backend API 对齐）
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
