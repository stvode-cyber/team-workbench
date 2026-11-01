namespace TeamWorkbench.Models;

/// <summary>
/// 治理门槛单条检查结果
/// </summary>
public class GateCheck
{
    public int GateId { get; set; }        // 1-5
    public string GateName { get; set; } = "";
    public bool Pass { get; set; }
    public string Detail { get; set; } = "";
}

/// <summary>
/// 治理检查整体结果
/// </summary>
public class GovernanceResult
{
    public bool AllPass { get; set; }
    public List<GateCheck> Gates { get; set; } = new();
}
