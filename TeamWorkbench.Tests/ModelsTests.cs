using System.Text.Json;
using System.Text.Json.Serialization;
using TeamWorkbench.Models;

namespace TeamWorkbench.Tests;

public class ModelsTests
{
    private static readonly JsonSerializerOptions SnakeCaseOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Agent_SnakeCaseJson_Deserialize()
    {
        var json = """
            {
                "name": "test-agent",
                "cn_name": "测试专岗",
                "priority": "P0",
                "install_status": "installed",
                "runtime_status": "busy",
                "depends_on": ["competitor-surveyor"]
            }
            """;
        var agent = JsonSerializer.Deserialize<Agent>(json, SnakeCaseOpts)!;

        Assert.Equal("test-agent", agent.Name);
        Assert.Equal("测试专岗", agent.CnName);
        Assert.Equal("P0", agent.Priority);
        Assert.Equal("installed", agent.InstallStatus);
        Assert.Equal("busy", agent.RuntimeStatus);
        Assert.Single(agent.DependsOn);
        Assert.Equal("competitor-surveyor", agent.DependsOn[0]);
    }

    [Fact]
    public void Agent_StatusLight_NotInstalled()
    {
        var agent = new Agent { InstallStatus = "not-installed", RuntimeStatus = "idle" };
        Assert.Equal("🔴", agent.StatusLight);
        Assert.Equal("未安装", agent.StatusLabel);
    }

    [Fact]
    public void Agent_StatusLight_Busy()
    {
        var agent = new Agent { InstallStatus = "installed", RuntimeStatus = "busy" };
        Assert.Equal("🟡", agent.StatusLight);
        Assert.Equal("忙碌中", agent.StatusLabel);
    }

    [Fact]
    public void Agent_StatusLight_Standby()
    {
        var agent = new Agent { InstallStatus = "installed", RuntimeStatus = "idle" };
        Assert.Equal("🟢", agent.StatusLight);
        Assert.Equal("待命", agent.StatusLabel);
    }

    [Fact]
    public void Agent_StatusLight_Partial()
    {
        // partial 也算已安装（🟢🟡切换看 runtime）
        var agent = new Agent { InstallStatus = "partial", RuntimeStatus = "idle" };
        Assert.Equal("🟢", agent.StatusLight);

        agent.RuntimeStatus = "busy";
        Assert.Equal("🟡", agent.StatusLight);
    }

    [Fact]
    public void Agent_RoundTripSerializeDeserialize()
    {
        var original = new Agent
        {
            Name = "drize-engineer",
            CnName = "推演引擎",
            Priority = "P0",
            RuntimeStatus = "idle",
            DependsOn = new List<string> { "competitor-surveyor", "project-taker" }
        };

        var json = JsonSerializer.Serialize(original, SnakeCaseOpts);
        var roundTrip = JsonSerializer.Deserialize<Agent>(json, SnakeCaseOpts)!;

        Assert.Equal(original.Name, roundTrip.Name);
        Assert.Equal(original.CnName, roundTrip.CnName);
        Assert.Equal(original.DependsOn.Count, roundTrip.DependsOn.Count);
        Assert.Contains("competitor-surveyor", roundTrip.DependsOn);
    }
}
