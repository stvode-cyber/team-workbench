namespace TeamWorkbench.Models;

/// <summary>
/// 触发条件：手动触发词 + 自动触发场景（对应 Node 的 how_to_trigger JSON）
/// </summary>
public class TriggerInfo
{
    public List<string> Manual { get; set; } = new();
    public List<string> Auto { get; set; } = new();
}

/// <summary>
/// Agent 完整 POCO —— 对应 SQLite agents 表 17 列
/// 与 Node db.ts agents 表列级完全对齐
/// </summary>
public class Agent
{
    // ===== 主键 + 基本信息 =====
    public int Id { get; set; }
    public string Name { get; set; } = "";       // 英文名（kebab-case）
    public string CnName { get; set; } = "";     // 中文名
    public string Priority { get; set; } = "";   // P0 / P1
    public string Emoji { get; set; } = "🤖";
    public string Desc { get; set; } = "";       // 一句话说明
    public string Role { get; set; } = "";       // 角色定义

    // ===== JSON 列（DB 里存 JSON 字符串，POCO 里是强类型对象）=====
    public TriggerInfo HowToTrigger { get; set; } = new();
    public List<string> Process { get; set; } = new();
    public List<string> Boundary { get; set; } = new();
    public List<string> Tools { get; set; } = new();
    public List<string> DependsOn { get; set; } = new();

    // ===== 状态 =====
    public string InstallStatus { get; set; } = "not-installed"; // installed / partial / not-installed
    public string? InstallDate { get; set; }                     // YYYY-MM-DD
    public string RuntimeStatus { get; set; } = "idle";          // idle / busy
    public string? LastSeen { get; set; }                        // ISO timestamp
    public string? UpdatedAt { get; set; }                       // ISO timestamp

    // ===== 派生属性（不进 DB，WPF 绑定用）=====

    /// <summary>
    /// 状态灯 emoji：🟢 已装待命 / 🟡 忙碌 / 🔴 未装
    /// </summary>
    public string StatusLight =>
        InstallStatus == "not-installed" ? "🔴" :
        RuntimeStatus == "busy" ? "🟡" : "🟢";

    /// <summary>
    /// 中文状态标签
    /// </summary>
    public string StatusLabel =>
        InstallStatus == "not-installed" ? "未安装" :
        RuntimeStatus == "busy" ? "忙碌中" : "待命";
}

/// <summary>
/// 面板顶部汇总统计
/// </summary>
public class AgentSummary
{
    public int Total { get; set; }
    public int Installed { get; set; }
    public int Partial { get; set; }
    public int Missing { get; set; }
    public int Busy { get; set; }
}
