using TeamWorkbench.Models;
using TeamWorkbench.Services;
using Xunit;

namespace TeamWorkbench.Tests;

/// <summary>
/// 每个测试共享一个临时 DB fixture（测试类级别，Dispose 清理）
/// </summary>
public class AgentRepositoryTests : IDisposable
{
    private readonly TempDbFixture _fixture;

    public AgentRepositoryTests()
    {
        _fixture = new TempDbFixture();
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Insert_And_GetByName_RoundTrip()
    {
        var agent = new Agent
        {
            Name = "test-agent",
            CnName = "测试专岗",
            Priority = "P1",
            RuntimeStatus = "idle",
            Role = "单元测试生成的假专岗"
        };
        var insertId = AgentRepository.Insert(agent);
        var fetched = AgentRepository.GetByName("test-agent");

        Assert.NotNull(fetched);
        Assert.Equal("test-agent", fetched!.Name);
        Assert.Equal("测试专岗", fetched.CnName);
        Assert.Equal(insertId, fetched.Id);
    }

    [Fact]
    public void GetAll_ReturnsSeededAgents_PlusInserted()
    {
        var seeded = AgentRepository.GetAll().Count;

        AgentRepository.Insert(new Agent { Name = "extra-1", CnName = "X", Priority = "P1", Role = "r1" });
        AgentRepository.Insert(new Agent { Name = "extra-2", CnName = "Y", Priority = "P0", Role = "r2" });

        Assert.Equal(seeded + 2, AgentRepository.GetAll().Count);
    }

    [Fact]
    public void Update_ChangesFields()
    {
        var agent = new Agent
        {
            Name = "upd-test", CnName = "原", Priority = "P1", Role = "r"
        };
        AgentRepository.Insert(agent);

        agent.CnName = "已更新";
        agent.Priority = "P0";
        AgentRepository.Update(agent);

        var fetched = AgentRepository.GetByName("upd-test")!;
        Assert.Equal("已更新", fetched.CnName);
        Assert.Equal("P0", fetched.Priority);
    }

    [Fact]
    public void UpdateInstallStatus_OnlyStatusField()
    {
        AgentRepository.Insert(new Agent { Name = "status-test", CnName = "S", Priority = "P1", Role = "r" });

        AgentRepository.UpdateInstallStatus("status-test", "installed");
        var f1 = AgentRepository.GetByName("status-test")!;
        Assert.Equal("installed", f1.InstallStatus);

        AgentRepository.UpdateInstallStatus("status-test", "partial");
        var f2 = AgentRepository.GetByName("status-test")!;
        Assert.Equal("partial", f2.InstallStatus);
    }

    [Fact]
    public void UpdateRuntimeStatus_OnlyRuntimeField()
    {
        AgentRepository.Insert(new Agent { Name = "rt-test", CnName = "S", Priority = "P1", Role = "r" });

        AgentRepository.UpdateRuntimeStatus("rt-test", "busy");
        var f1 = AgentRepository.GetByName("rt-test")!;
        Assert.Equal("busy", f1.RuntimeStatus);

        AgentRepository.UpdateRuntimeStatus("rt-test", "idle");
        var f2 = AgentRepository.GetByName("rt-test")!;
        Assert.Equal("idle", f2.RuntimeStatus);
    }

    [Fact]
    public void Delete_RemovesRow()
    {
        AgentRepository.Insert(new Agent { Name = "del-test", CnName = "D", Priority = "P1", Role = "r" });
        Assert.NotNull(AgentRepository.GetByName("del-test"));

        AgentRepository.Delete("del-test");
        Assert.Null(AgentRepository.GetByName("del-test"));
    }

    [Fact]
    public void Summary_CountsCorrect()
    {
        // 临时库里 Seed 会插 11 条，默认全 not-installed + idle
        var summary = AgentRepository.GetSummary();
        Assert.True(summary.Total >= 11);

        // 插一条 installed 看看 summary 变化
        AgentRepository.Insert(new Agent
        {
            Name = "sum-test", CnName = "S", Priority = "P1", Role = "r",
            InstallStatus = "installed", RuntimeStatus = "busy"
        });
        var s2 = AgentRepository.GetSummary();
        Assert.Equal(summary.Installed + 1, s2.Installed);
        Assert.Equal(summary.Busy + 1, s2.Busy);
    }

    [Fact]
    public void GetByName_NotFound_ReturnsNull()
    {
        Assert.Null(AgentRepository.GetByName("no-such-agent-xyz"));
    }

    [Fact]
    public void Insert_DuplicateName_Fails()
    {
        AgentRepository.Insert(new Agent { Name = "dup", CnName = "A", Priority = "P1", Role = "r" });

        // SQLite UNIQUE constraint → 应抛异常（测试里 catch 验证）
        Assert.ThrowsAny<Exception>(() =>
            AgentRepository.Insert(new Agent { Name = "dup", CnName = "B", Priority = "P1", Role = "r2" }));
    }
}
