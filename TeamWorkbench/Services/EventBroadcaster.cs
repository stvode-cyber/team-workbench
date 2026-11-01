using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TeamWorkbench.Data;

namespace TeamWorkbench.Services;

/// <summary>
/// 统一事件广播入口
/// - 所有 install/scan/runtime/create/delete 事件都走这里
/// - 同时写 SQLite status_events 表 + 推 WebSocket
/// - 线程安全（_lock 保护 ws 引用）
/// </summary>
public static class EventBroadcaster
{
    private static WebSocket? _ws;
    private static readonly object _lock = new();

    /// <summary>
    /// WebSocketServer 连接时注册
    /// </summary>
    public static void SetWs(WebSocket ws)
    {
        lock (_lock) _ws = ws;
    }

    /// <summary>
    /// WebSocketServer 断开时清理
    /// </summary>
    public static void ClearWs(WebSocket ws)
    {
        lock (_lock)
        {
            if (_ws == ws) _ws = null;
        }
    }

    /// <summary>
    /// 广播事件（同时写 DB + 推 WS）
    /// 非阻塞：WS 发送用 fire-and-forget（Task.Run）
    /// </summary>
    public static void Broadcast(string eventType, string agentName, object? eventData = null)
    {
        var dataJson = eventData == null ? "{}" : JsonSerializer.Serialize(eventData);

        // 1. 写 DB（同步，轻量）
        try
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO status_events (agent_name, event_type, event_data, created_at) " +
                              "VALUES ($name, $type, $data, datetime('now'))";
            cmd.Parameters.AddWithValue("$name", agentName);
            cmd.Parameters.AddWithValue("$type", eventType);
            cmd.Parameters.AddWithValue("$data", dataJson);
            cmd.ExecuteNonQuery();
        }
        catch { /* DB 写入失败不阻塞 WS 广播 */ }

        // 2. 推 WS（异步 fire-and-forget）
        var payload = JsonSerializer.Serialize(new
        {
            type = eventType,
            agent_name = agentName,
            data = eventData,
            created_at = DateTime.Now.ToString("o")
        });

        _ = SendToWsAsync(payload);
    }

    private static async Task SendToWsAsync(string json)
    {
        WebSocket? ws;
        lock (_lock) ws = _ws;

        if (ws == null || ws.State != WebSocketState.Open) return;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // 断开由 WebSocketServer 自己清理
        }
    }
}
