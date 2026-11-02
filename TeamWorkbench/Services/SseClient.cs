using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace TeamWorkbench.Services;

/// <summary>
/// SSE 客户端（精确对齐指南针 L431-521）
/// 
/// .NET 10 没有内建 EventSource 类 → 手动实现 SSE 协议解析：
///   : ping          → 心跳注释（忽略，触发 OnHeartbeat）
///   event: xxx      → 事件类型
///   data: json      → 一行或多行 JSON 负载
///   id: xxx         → 事件 ID（断线重连用 Last-Event-ID）
///   retry: ms       → 服务端建议的重连间隔
///   空行            → 一个 event 结束，触发 OnEvent
/// 
/// 断线重连：指数退避 3s→6s→12s→24s→30s cap
/// </summary>
public class SseClient : IAsyncDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private CancellationTokenSource? _cts;
    private string _lastEventId = "";
    private int _retryDelayMs = 3_000;  // 3s 起步

    public event EventHandler<SseEvent>? OnEvent;
    public event EventHandler? OnHeartbeat;

    public async Task ConnectAsync(Uri endpoint, HttpContent content, CancellationToken externalToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Content = content;
                request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
                if (!string.IsNullOrEmpty(_lastEventId))
                    request.Headers.TryAddWithoutValidation("Last-Event-ID", _lastEventId);

                using var resp = await _http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, _cts.Token
                );
                resp.EnsureSuccessStatusCode();

                await using var stream = await resp.Content.ReadAsStreamAsync(_cts.Token);
                using var reader = new StreamReader(stream);
                _retryDelayMs = 3_000;  // 连接成功 → 重置退避
                await ParseSseLoopAsync(reader);
            }
            catch (OperationCanceledException) { return; }
            catch (HttpRequestException)
            {
                await Task.Delay(_retryDelayMs, _cts.Token);
                _retryDelayMs = Math.Min(_retryDelayMs * 2, 30_000);  // 指数退避 → cap 30s
            }
        }
    }

    private async Task ParseSseLoopAsync(StreamReader reader)
    {
        var current = new SseEvent();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (_cts?.IsCancellationRequested ?? true) return;

            // : 开头 → 心跳注释
            if (line.StartsWith(':'))
            {
                OnHeartbeat?.Invoke(this, EventArgs.Empty);
                continue;
            }

            // 空行 → 一个 event 结束
            if (string.IsNullOrWhiteSpace(line))
            {
                if (current.Data != null)
                {
                    _lastEventId = current.Id ?? _lastEventId;
                    OnEvent?.Invoke(this, current);
                }
                current = new SseEvent { RetryDelayMs = _retryDelayMs };
                continue;
            }

            // 解析 field: value
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var field = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            switch (field)
            {
                case "event": current.EventType = value; break;
                case "data":  current.Data = current.Data == null ? value : current.Data + "\n" + value; break;
                case "id":    current.Id = value; break;
                case "retry": if (int.TryParse(value, out var ms)) _retryDelayMs = ms; break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _http.Dispose();
        await ValueTask.CompletedTask;
    }
}
