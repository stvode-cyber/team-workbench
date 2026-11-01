using System.Text.Json;
using TeamWorkbench.Data;
using TeamWorkbench.Services;

namespace TeamWorkbench.Tests;

/// <summary>
/// EventBroadcaster 单元测试
/// · Broadcast 写 DB status_events 表
/// · Broadcast 触发 C# 进程内 OnEventFired 事件
/// · null WebSocket 时安全不崩
/// · DB 写入失败不阻塞（try-catch 吞掉）
/// </summary>
public class EventBroadcasterTests : IDisposable
{
    private readonly TempDbFixture _fixture;

    public EventBroadcasterTests()
    {
        _fixture = new TempDbFixture();
    }

    public void Dispose() => _fixture.Dispose();

    // ========== 基础：Broadcast 写 DB ==========

    [Fact]
    public void Broadcast_WritesStatusEventsToDb()
    {
        EventBroadcaster.Broadcast("install", "test-agent", new { ok = true });

        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM status_events WHERE event_type='install' AND agent_name='test-agent'";
        int count = Convert.ToInt32(cmd.ExecuteScalar());

        Assert.Equal(1, count);
    }

    [Fact]
    public void Broadcast_EventDataSerializedAsJson()
    {
        var payload = new { action = "install", ok = true, copied = new[] { "a.md", "s.md" } };
        EventBroadcaster.Broadcast("install", "test-agent", payload);

        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT event_data FROM status_events WHERE agent_name='test-agent' ORDER BY id DESC LIMIT 1";
        var dataJson = (string)cmd.ExecuteScalar()!;

        var doc = JsonDocument.Parse(dataJson);
        Assert.True(doc.RootElement.TryGetProperty("action", out var action));
        Assert.Equal("install", action.GetString());
        Assert.True(doc.RootElement.TryGetProperty("ok", out var ok));
        Assert.True(ok.GetBoolean());
    }

    [Fact]
    public void Broadcast_NullEventData_StoresEmptyJsonObject()
    {
        EventBroadcaster.Broadcast("runtime", "some-agent");

        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT event_data FROM status_events WHERE agent_name='some-agent' ORDER BY id DESC LIMIT 1";
        var dataJson = (string)cmd.ExecuteScalar()!;

        Assert.Equal("{}", dataJson);
    }

    [Fact]
    public void Broadcast_MultipleEvents_AllInserted()
    {
        var before = CountStatusEvents();

        EventBroadcaster.Broadcast("install", "evt-a");
        EventBroadcaster.Broadcast("scan", "evt-b");
        EventBroadcaster.Broadcast("runtime", "evt-c");

        Assert.Equal(before + 3, CountStatusEvents());
    }

    private static int CountStatusEvents()
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM status_events";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ========== C# 进程内事件桥 ==========
    // 注意：static event 会跨测试实例残留订阅，每个测试必须 try-finally 取消订阅

    [Fact]
    public void Broadcast_FiresOnEventFired()
    {
        string? receivedType = null;
        string? receivedName = null;
        object? receivedData = null;

        Action<string, string, object?> handler = (t, n, d) =>
        {
            receivedType = t;
            receivedName = n;
            receivedData = d;
        };

        EventBroadcaster.OnEventFired += handler;
        try
        {
            var payload = new { count = 42 };
            EventBroadcaster.Broadcast("scan", "drize-engineer", payload);

            Assert.Equal("scan", receivedType);
            Assert.Equal("drize-engineer", receivedName);
            Assert.NotNull(receivedData);
        }
        finally { EventBroadcaster.OnEventFired -= handler; }
    }

    [Fact]
    public void Broadcast_MultipleSubscribers_AllCalled()
    {
        int callCount = 0;
        Action<string, string, object?> h1 = (_, _, _) => callCount++;
        Action<string, string, object?> h2 = (_, _, _) => callCount++;
        Action<string, string, object?> h3 = (_, _, _) => callCount++;

        EventBroadcaster.OnEventFired += h1;
        EventBroadcaster.OnEventFired += h2;
        EventBroadcaster.OnEventFired += h3;
        try
        {
            EventBroadcaster.Broadcast("create", "new-agent");
            Assert.Equal(3, callCount);
        }
        finally
        {
            EventBroadcaster.OnEventFired -= h1;
            EventBroadcaster.OnEventFired -= h2;
            EventBroadcaster.OnEventFired -= h3;
        }
    }

    [Fact]
    public void Broadcast_SubscriberException_DoesNotBlockDBWrite()
    {
        Action<string, string, object?> boom = (_, _, _) => throw new InvalidOperationException("boom");
        EventBroadcaster.OnEventFired += boom;
        try
        {
            // 不应抛异常 —— Broadcast 内部 try-catch 吞掉订阅者异常
            EventBroadcaster.Broadcast("runtime", "a");
        }
        finally { EventBroadcaster.OnEventFired -= boom; }

        // DB 写入应该成功
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM status_events WHERE event_type='runtime' AND agent_name='a'";
        int count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1, count);
    }

    // ========== WS 安全 ==========

    [Fact]
    public void Broadcast_NoWebSocket_StillWorks()
    {
        EventBroadcaster.Broadcast("delete", "ghost-agent");

        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM status_events WHERE agent_name='ghost-agent'";
        int count = Convert.ToInt32(cmd.ExecuteScalar());

        Assert.Equal(1, count);
    }
}
