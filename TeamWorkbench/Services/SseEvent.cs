namespace TeamWorkbench.Services;

/// <summary>
/// SSE 事件模型（精确对齐指南针 L487-520 解析结果）
/// 对应 JSON payload = SseEvent.Data 反序列化
/// </summary>
public class SseEvent
{
    public string EventType { get; set; } = "";      // run_started / agent_started / agent_completed / ...
    public string? Data { get; set; }                // 原始 JSON 字符串
    public string? Id { get; set; }                  // 事件 ID（断线重连用）
    public int RetryDelayMs { get; set; } = 3000;    // 服务端建议的重连间隔
}
