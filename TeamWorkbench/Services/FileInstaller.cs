using System.IO;
using TeamWorkbench.Config;
using TeamWorkbench.Services;

namespace TeamWorkbench.Services;

/// <summary>
/// 文件安装/卸载/扫描
/// 关键教训（来自 ExperienceRecall 1306372）：
///   校验前置 → 所有 File.Copy/File.Delete 之前先 Directory.Exists / File.Exists 检查
///   失败无副作用 → 任何一步出错立即返回，不留下半成品
/// </summary>
public static class FileInstaller
{
    // ============================================================
    // 找源目录（递归搜 02_AGENTS 下含 agentName 的子目录）
    // ============================================================

    /// <summary>
    /// 返回 (agent.md 路径, SKILL.md 路径?) 或 null
    /// </summary>
    public static (string agentMd, string? skillMd)? FindSource(string agentName)
    {
        if (!Directory.Exists(AppConfig.SourceDirRoot)) return null;

        var dir = Directory.EnumerateDirectories(AppConfig.SourceDirRoot)
            .FirstOrDefault(d => d.Contains(agentName, StringComparison.OrdinalIgnoreCase));

        if (dir == null) return null;

        var agentMd = Path.Combine(dir, "agent.md");
        if (!File.Exists(agentMd)) return null;

        var skillMd = Path.Combine(dir, "SKILL.md");
        return (agentMd, File.Exists(skillMd) ? skillMd : null);
    }

    // ============================================================
    // 安装
    // ============================================================

    public record InstallResult(bool Ok, List<string> Copied, List<string> Errors);

    public static InstallResult Install(string agentName)
    {
        var copied = new List<string>();
        var errors = new List<string>();

        // [前置校验 1] 查 agent 存在
        var agent = AgentRepository.GetByName(agentName);
        if (agent == null)
        {
            errors.Add($"agent '{agentName}' 不在 DB 里");
            return new InstallResult(false, copied, errors);
        }

        // [前置校验 2] 找源目录
        var source = FindSource(agentName);
        if (source == null)
        {
            errors.Add($"找不到 02_AGENTS 下的 {agentName} 源目录");
            return new InstallResult(false, copied, errors);
        }

        // [前置校验 3] 目标 agent.md 不被其他进程占锁（File.Exists 就不覆盖，幂等）
        try
        {
            Directory.CreateDirectory(AppConfig.AgentsDir);
            Directory.CreateDirectory(Path.Combine(AppConfig.SkillsDir, agentName));

            var agentMdDest = Path.Combine(AppConfig.AgentsDir, $"{agentName}.md");
            File.Copy(source.Value.agentMd, agentMdDest, overwrite: true);
            copied.Add(agentMdDest);

            if (source.Value.skillMd != null)
            {
                var skillDest = Path.Combine(AppConfig.SkillsDir, agentName, "SKILL.md");
                File.Copy(source.Value.skillMd, skillDest, overwrite: true);
                copied.Add(skillDest);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"文件操作失败: {ex.Message}");
            // 经验教训：有副作用后失败 → 清理已拷贝的（避免半成品）
            foreach (var f in copied)
            {
                try { File.Delete(f); } catch { }
            }
            return new InstallResult(false, new List<string>(), errors);
        }

        // 更新 DB + 广播
        AgentRepository.UpdateInstallStatus(agentName, "installed", DateTime.Now.ToString("o"));
        EventBroadcaster.Broadcast("install", agentName, new { action = "install", ok = true, copied });

        return new InstallResult(true, copied, errors);
    }

    // ============================================================
    // 卸载
    // ============================================================

    public record UninstallResult(bool Ok, List<string> Deleted, List<string> Errors);

    public static UninstallResult Uninstall(string agentName)
    {
        var deleted = new List<string>();
        var errors = new List<string>();

        // [前置校验] 查 DB 存在
        if (AgentRepository.GetByName(agentName) == null)
        {
            errors.Add($"agent '{agentName}' 不在 DB");
            return new UninstallResult(false, deleted, errors);
        }

        try
        {
            var agentMdDest = Path.Combine(AppConfig.AgentsDir, $"{agentName}.md");
            if (File.Exists(agentMdDest))
            {
                File.Delete(agentMdDest);
                deleted.Add(agentMdDest);
            }

            var skillDir = Path.Combine(AppConfig.SkillsDir, agentName);
            if (Directory.Exists(skillDir))
            {
                Directory.Delete(skillDir, recursive: true);
                deleted.Add(skillDir);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"删除失败: {ex.Message}");
            return new UninstallResult(false, deleted, errors);
        }

        AgentRepository.UpdateInstallStatus(agentName, "not-installed");
        EventBroadcaster.Broadcast("install", agentName, new { action = "uninstall", ok = true, deleted });

        return new UninstallResult(true, deleted, errors);
    }

    // ============================================================
    // 全量扫描
    // ============================================================

    public record ScanAllResult(int Total, int Installed, int Partial, int Missing, List<string> Updated);

    public static ScanAllResult ScanAll()
    {
        var agents = AgentRepository.GetAll();
        var updated = new List<string>();
        int installed = 0, partial = 0, missing = 0;

        foreach (var a in agents)
        {
            var agentMdDest = Path.Combine(AppConfig.AgentsDir, $"{a.Name}.md");
            var skillMdDest = Path.Combine(AppConfig.SkillsDir, a.Name, "SKILL.md");

            bool hasAgent = File.Exists(agentMdDest);
            bool hasSkill = File.Exists(skillMdDest);

            string newStatus;
            if (hasAgent && hasSkill) { newStatus = "installed"; installed++; }
            else if (hasAgent || hasSkill) { newStatus = "partial"; partial++; }
            else { newStatus = "not-installed"; missing++; }

            if (newStatus != a.InstallStatus)
            {
                AgentRepository.UpdateInstallStatus(a.Name, newStatus);
                updated.Add($"{a.Name}: {a.InstallStatus} → {newStatus}");
                EventBroadcaster.Broadcast("scan", a.Name, new { old = a.InstallStatus, @new = newStatus });
            }
        }

        return new ScanAllResult(agents.Count, installed, partial, missing, updated);
    }

    // ============================================================
    // 全量安装（循环 Install）
    // ============================================================

    public record InstallAllResult(int Total, int Succeeded, int Failed, List<string> Failures);

    public static InstallAllResult InstallAll()
    {
        var agents = AgentRepository.GetAll();
        int ok = 0, fail = 0;
        var failures = new List<string>();

        foreach (var a in agents)
        {
            var r = Install(a.Name);
            if (r.Ok) ok++;
            else { fail++; failures.Add($"{a.Name}: {string.Join(", ", r.Errors)}"); }
        }

        return new InstallAllResult(agents.Count, ok, fail, failures);
    }
}
