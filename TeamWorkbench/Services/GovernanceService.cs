using System.IO;
using System.Text.RegularExpressions;
using TeamWorkbench.Models;

namespace TeamWorkbench.Services;

/// <summary>
/// 新增专岗管制协议 · 5 条硬门槛
/// 对应 Node agents.ts 的 runGovernanceCheck 函数
/// </summary>
public static partial class GovernanceService
{
    // Gate 5: kebab-case 正则
    private static readonly Regex KebabCase = new(@"^[a-z][a-z0-9-]*[a-z0-9]$", RegexOptions.Compiled);

    // 源目录扫描用的 agent 名列表（递归 walk PROJECT_ROOT 下含 agentName 的目录）
    // C# 版直接用 Directory.GetDirectories 递归，Node 版用 findAgentSourceDir 递归 walk

    /// <summary>
    /// 跑 5 条治理门槛检查
    /// </summary>
    /// <param name="candidate">待创建/更新的 agent 数据</param>
    /// <param name="excludeName">更新时排除自己（避免重名误报）</param>
    /// <param name="projectRoot">源目录扫描根路径</param>
    public static GovernanceResult RunGateChecks(
        Agent candidate,
        string? excludeName = null,
        string? projectRoot = null)
    {
        var gates = new List<GateCheck>();
        var name = candidate.Name.Trim().ToLowerInvariant();

        // ========== Gate 1: 英文名唯一 ==========
        bool nameConflict = false;
        var dbRow = AgentRepository.GetByName(name);
        if (dbRow != null && dbRow.Name != excludeName) nameConflict = true;

        // 也扫源目录（如果给了 projectRoot）
        if (!nameConflict && !string.IsNullOrEmpty(projectRoot))
        {
            nameConflict = SourceDirExists(name, projectRoot);
        }

        gates.Add(new GateCheck
        {
            GateId = 1, GateName = "英文名唯一",
            Pass = !nameConflict,
            Detail = nameConflict ? $"重名：{name} 已存在（DB 或源目录）" : "OK",
        });

        // ========== Gate 2: 能力补位（无抢活）—— 软冲突 ==========
        var overlaps = new List<string>();
        var roleText = (candidate.Role ?? "").ToLowerInvariant();
        var allAgents = AgentRepository.GetAll();

        foreach (var a in allAgents)
        {
            if (excludeName != null && a.Name == excludeName) continue;
            var aRole = (a.Role ?? "").ToLowerInvariant();
            var sharedTools = (candidate.Tools ?? []).Count(t => a.Tools.Contains(t));

            if (sharedTools >= 2 && roleText.Length > 0 && aRole.Length > 0)
            {
                var keywords = roleText
                    .Split([' ', ',', '，', '。', '、', '；', ';'], StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 2)
                    .ToList();
                var collides = keywords.Count(k => aRole.Contains(k));
                if (collides >= 2)
                {
                    overlaps.Add($"{a.CnName}({a.Name}) — 共享工具 {sharedTools} 个 + 角色关键词撞 {collides} 个");
                }
            }
        }

        gates.Add(new GateCheck
        {
            GateId = 2, GateName = "能力补位（无抢活）",
            Pass = overlaps.Count == 0,
            Detail = overlaps.Count > 0 ? string.Join("; ", overlaps) : "OK（未检测到抢活）",
        });

        // ========== Gate 3: 有完整 Skill 支撑 ==========
        var requiredOk =
            !string.IsNullOrWhiteSpace(candidate.Role) && candidate.Role.Trim().Length >= 10 &&
            (candidate.HowToTrigger.Manual?.Count >= 1) &&
            (candidate.HowToTrigger.Auto?.Count >= 1) &&
            (candidate.Boundary?.Count >= 1);

        gates.Add(new GateCheck
        {
            GateId = 3, GateName = "有完整 Skill 支撑（role + trigger + boundary）",
            Pass = requiredOk,
            Detail = requiredOk ? "OK" : "role<10字 或 trigger/boundary 为空",
        });

        // ========== Gate 4: 优先级合法 ==========
        var priorityOk = candidate.Priority == "P0" || candidate.Priority == "P1";
        gates.Add(new GateCheck
        {
            GateId = 4, GateName = "优先级合法（P0/P1）",
            Pass = priorityOk,
            Detail = priorityOk ? "OK" : $"非法优先级：{candidate.Priority}",
        });

        // ========== Gate 5: 英文名格式合法（kebab-case）==========
        var nameFormatOk = KebabCase.IsMatch(name);
        gates.Add(new GateCheck
        {
            GateId = 5, GateName = "英文名格式合法（kebab-case）",
            Pass = nameFormatOk,
            Detail = nameFormatOk ? "OK" : $"英文名必须小写+连字符：{name}",
        });

        var allPass = gates.All(g => g.Pass);
        return new GovernanceResult { AllPass = allPass, Gates = gates };
    }

    /// <summary>
    /// 递归扫描 projectRoot 下是否存在包含 agentName 的子目录（里面要有 agent.md）
    /// 对应 Node findAgentSourceDir 函数
    /// </summary>
    private static bool SourceDirExists(string agentName, string rootPath)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
            {
                if (dir.Contains(agentName, StringComparison.OrdinalIgnoreCase))
                {
                    var agentMd = Path.Combine(dir, "agent.md");
                    if (File.Exists(agentMd)) return true;
                }
            }
        }
        catch { /* 权限/路径问题：静默跳过 */ }
        return false;
    }
}
