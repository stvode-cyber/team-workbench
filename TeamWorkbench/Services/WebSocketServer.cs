using System.Net;
using System.Net.WebSockets;

namespace TeamWorkbench.Services;

/// <summary>
/// WebSocket /events 端点
/// MVP 只支持 1 个客户端（新连接自动踢旧的）
/// 由 HttpServer 在 GET /events 时调用
/// </summary>
public static class WebSocketServer
{
    public static async Task HandleAsync(HttpListenerContext ctx)
    {
        if (!ctx.Request.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        WebSocket ws;
        try
        {
            var wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null);
            ws = wsContext.WebSocket;
        }
        catch
        {
            ctx.Response.StatusCode = 500;
            ctx.Response.Close();
            return;
        }

        // 注册给 EventBroadcaster
        EventBroadcaster.SetWs(ws);

        // 发送欢迎帧
        try
        {
            var welcome = "{\n  \"type\": \"connected\",\n  \"data\": { \"msg\": \"WebSocket /events 已连接\" }\n}";
            var welcomeBytes = System.Text.Encoding.UTF8.GetBytes(welcome);
            await ws.SendAsync(welcomeBytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }

        // Keepalive loop（接收客户端 ping，MVP 不强制）
        var buffer = new byte[1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    break;
                }
                // MVP 不处理客户端发来的消息（只服务端推）
            }
        }
        catch { /* 正常断开 */ }
        finally
        {
            EventBroadcaster.ClearWs(ws);
            try { ws.Dispose(); } catch { }
        }
    }
}
