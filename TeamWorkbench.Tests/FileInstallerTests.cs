using System.IO;
using TeamWorkbench.Data;
using TeamWorkbench.Models;
using TeamWorkbench.Services;

namespace TeamWorkbench.Tests;

/// <summary>
/// FileInstaller 单元测试
/// · FindSource 找源目录 / 找不到 null
/// · Install 成功 + 找不到源目录失败 + DB 里 agent 不存在失败
/// · Uninstall 成功 + DB 不存在失败
/// · ScanOne 根据文件存在性判断 install_status 并更新 DB
/// · ScanAll 全量扫 + 全安装
/// </summary>
public class FileInstallerTests : IDisposable
{
    private readonly TempDbFixture _db;
    private readonly string _sourceDir;       // 临时 02_AGENTS 根目录
    private readonly string _agentsDir;        // 临时目标 .trae-cn/agents
    private readonly string _skillsDir;       // 临时目标 .trae-cn/skills

    public FileInstallerTests()
    {
        _db = new TempDbFixture();

        // 临时目录（全在 TempPath 下，互不影响其他测试）
        var root = Path.Combine(Path.GetTempPath(), $"tw_fi_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(root, "02_AGENTS");
        _agentsDir = Path.Combine(root, "agents");
        _skillsDir = Path.Combine(root, "skills");

        Directory.CreateDirectory(_sourceDir);

        // 注入 override（让 FileInstaller 走临时目录，而不是 AppConfig 硬编码）
        FileInstaller.SourceDirRootOverride = _sourceDir;
        FileInstaller.AgentsDirOverride = _agentsDir;
        FileInstaller.SkillsDirOverride = _skillsDir;
    }

    public void Dispose()
    {
        // 清理 override（避免残留影响后续测试类）
        FileInstaller.SourceDirRootOverride = null;
        FileInstaller.AgentsDirOverride = null;
        FileInstaller.SkillsDirOverride = null;

        _db.Dispose();

        try { if (Directory.Exists(_sourceDir)) Directory.Delete(Path.GetDirectoryName(_sourceDir)!, recursive: true); } catch { }
    }

    // 辅助：造一个源目录（agent.md + 可选 SKILL.md）
    private void CreateSource(string agentName, bool withSkill = true)
    {
        var dir = Path.Combine(_sourceDir, $"99_{agentName}"); // 前缀确保 Directory.Contains(agentName) 能搜到
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "agent.md"), $"# {agentName} agent");
        if (withSkill) File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"# {agentName} skill");
    }

    private void CreateAgentInDb(string name, string cn, string priority = "P1")
    {
        AgentRepository.Insert(new Agent
        {
            Name = name, CnName = cn, Priority = priority,
            Role = "测试角色描述足够长满足 Gate 3 门槛字数"
        });
    }

    // ========== FindSource ==========

    [Fact]
    public void FindSource_ReturnsBothFiles()
    {
        CreateSource("test-agent", withSkill: true);

        var result = FileInstaller.FindSource("test-agent");
        Assert.NotNull(result);
        Assert.EndsWith("agent.md", result.Value.agentMd);
        Assert.NotNull(result.Value.skillMd);
        Assert.EndsWith("SKILL.md", result.Value.skillMd!);
    }

    [Fact]
    public void FindSource_SkillMdOptional()
    {
        CreateSource("no-skill-agent", withSkill: false);

        var result = FileInstaller.FindSource("no-skill-agent");
        Assert.NotNull(result);
        Assert.EndsWith("agent.md", result.Value.agentMd);
        Assert.Null(result.Value.skillMd);
    }

    [Fact]
    public void FindSource_NotFound_ReturnsNull()
    {
        var result = FileInstaller.FindSource("ghost-agent-xyz");
        Assert.Null(result);
    }

    // ========== Install ==========

    [Fact]
    public void Install_Success_CopiesFilesAndUpdatesDb()
    {
        // Seed 里有 drize-engineer，但没源目录 → 创建临时源
        var name = "drize-engineer"; // Seed 里已有
        CreateSource(name);

        var r = FileInstaller.Install(name);
        Assert.True(r.Ok);
        Assert.Empty(r.Errors);
        Assert.Equal(2, r.Copied.Count);

        // 验证目标文件真存在
        Assert.True(File.Exists(Path.Combine(_agentsDir, $"{name}.md")));
        Assert.True(File.Exists(Path.Combine(_skillsDir, name, "SKILL.md")));

        // 验证 DB 也更新了
        var a = AgentRepository.GetByName(name)!;
        Assert.Equal("installed", a.InstallStatus);
    }

    [Fact]
    public void Install_AgentNotInDb_Fails()
    {
        CreateSource("ghost-install");

        var r = FileInstaller.Install("ghost-install");
        Assert.False(r.Ok);
        Assert.Single(r.Errors);
        Assert.Contains("不在 DB", r.Errors[0]);
    }

    [Fact]
    public void Install_SourceNotFound_Fails()
    {
        CreateAgentInDb("no-source-agent", "无源");

        var r = FileInstaller.Install("no-source-agent");
        Assert.False(r.Ok);
        Assert.Contains("找不到", r.Errors[0]);
    }

    // ========== Uninstall ==========

    [Fact]
    public void Uninstall_Success_DeletesFilesAndUpdatesDb()
    {
        var name = "arch-designer"; // Seed 里已有
        CreateSource(name);
        FileInstaller.Install(name); // 先装上
        Assert.True(File.Exists(Path.Combine(_agentsDir, $"{name}.md")));

        var r = FileInstaller.Uninstall(name);
        Assert.True(r.Ok);
        Assert.False(File.Exists(Path.Combine(_agentsDir, $"{name}.md")));

        // DB 恢复 not-installed
        var a = AgentRepository.GetByName(name)!;
        Assert.Equal("not-installed", a.InstallStatus);
    }

    [Fact]
    public void Uninstall_AgentNotInDb_Fails()
    {
        var r = FileInstaller.Uninstall("ghost-uninstall");
        Assert.False(r.Ok);
        Assert.Contains("不在 DB", r.Errors[0]);
    }

    // ========== ScanOne ==========

    [Fact]
    public void ScanOne_NoFiles_NotInstalled()
    {
        var name = "ledger-keeper"; // Seed 里有，但没源目录也没装
        var r = FileInstaller.ScanOne(name);
        Assert.True(r.Ok);
        Assert.Equal("not-installed", r.NewStatus);
    }

    [Fact]
    public void ScanOne_UpdatedWhenStatusChanges()
    {
        // 手动造安装好的文件，但 DB 状态落后（模拟"文件被外部装进
        // 来了但 DB 没跟上"的真实场景）
        var name = "rca-analyst";
        CreateSource(name);
        Directory.CreateDirectory(_agentsDir);
        Directory.CreateDirectory(Path.Combine(_skillsDir, name));
        File.WriteAllText(Path.Combine(_agentsDir, $"{name}.md"), "# test");
        File.WriteAllText(Path.Combine(_skillsDir, name, "SKILL.md"), "# test");

        // 把 DB 手动设成 not-installed，模拟状态落后
        AgentRepository.UpdateInstallStatus(name, "not-installed");
        var before = AgentRepository.GetByName(name)!.InstallStatus;

        var r = FileInstaller.ScanOne(name);

        Assert.True(r.Updated);
        Assert.NotEqual(before, r.NewStatus);
        Assert.Equal("installed", r.NewStatus);
    }

    [Fact]
    public void ScanOne_NoChangeWhenStatusMatches()
    {
        var name = "safety-auditor";
        CreateSource(name);
        FileInstaller.Install(name);
        FileInstaller.ScanOne(name); // 第一次更新 DB

        var r2 = FileInstaller.ScanOne(name); // 第二次扫
        Assert.False(r2.Updated); // DB 已经是 installed，不更新
    }

    // ========== ScanAll ==========

    [Fact]
    public void ScanAll_AllSeededAgents_NotInstalled()
    {
        var r = FileInstaller.ScanAll();
        Assert.True(r.Total >= 11); // Seed 11 条
        Assert.True(r.Missing >= 11); // 没装过
        Assert.Equal(0, r.Installed);
    }
}
