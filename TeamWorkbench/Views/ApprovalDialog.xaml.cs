using System.Windows;

namespace TeamWorkbench.Views;

/// <summary>
/// Day 16: HITL 人类审批对话框
/// · 深色主题，✅ 批准 / ❌ 拒绝 双按钮
/// · Close 按钮 = 拒绝（DialogResult=false）
/// · ShowDialog() 返回 DialogResult（true=批准，false=拒绝）
/// 
/// 由 DispatchCenterView 串行打开：
/// 多个 approval_pending 入队，一个关了才开下一个
/// </summary>
public partial class ApprovalDialog : Window
{
    public int ApprovalId { get; }
    public string AgentName { get; }
    public bool ApproveClicked { get; private set; }

    public ApprovalDialog(int approvalId, string agentName, string? description = null)
    {
        InitializeComponent();

        ApprovalId = approvalId;
        AgentName = agentName;

        AgentNameText.Text = $"Agent [{agentName}] 需要人类审批";
        ApprovalIdText.Text = $"approval_id = {approvalId}";

        if (!string.IsNullOrWhiteSpace(description))
            DescriptionText.Text = description;
    }

    private void ApproveBtn_Click(object sender, RoutedEventArgs e)
    {
        ApproveClicked = true;
        DialogResult = true;
        Close();
    }

    private void RejectBtn_Click(object sender, RoutedEventArgs e)
    {
        ApproveClicked = false;
        DialogResult = false;
        Close();
    }
}
