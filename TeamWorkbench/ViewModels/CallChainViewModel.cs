using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using TeamWorkbench.Models;
using TeamWorkbench.Services;

namespace TeamWorkbench.ViewModels;

/// <summary>
/// 调用链画布 ViewModel
/// · 11 个节点（Agent）+ depends_on 边
/// · SaveToDb 写回 AgentRepository.Update
/// </summary>
public class CallChainViewModel : ObservableObject
{
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<EdgeViewModel> Edges { get; } = new();

    private string _status = "就绪";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    // ===== 从 AgentRepository 加载 =====
    public void LoadFromAgents()
    {
        Nodes.Clear();
        Edges.Clear();

        var agents = AgentRepository.GetAll();

        // 11 节点 → Canvas 网格布局（3 列 × 4 行，第 4 行 3 个）
        int cols = 3;
        double cellW = 260;
        double cellH = 130;
        double startX = 120;
        double startY = 80;

        for (int i = 0; i < agents.Count; i++)
        {
            var a = agents[i];
            int col = i % cols;
            int row = i / cols;
            Nodes.Add(new NodeViewModel
            {
                AgentId = a.Id,
                AgentName = a.Name,
                CnName = a.CnName,
                Emoji = a.Emoji,
                Priority = a.Priority,
                Agent = a,
                X = startX + col * cellW,
                Y = startY + row * cellH,
            });
        }

        // 反推边：遍历每个 Agent.DependsOn → 创建 Edge
        foreach (var a in agents)
        {
            if (a.DependsOn == null || a.DependsOn.Count == 0) continue;
            var sourceNode = Nodes.FirstOrDefault(n => n.AgentName == a.Name);
            if (sourceNode == null) continue;
            foreach (var dep in a.DependsOn)
            {
                var targetNode = Nodes.FirstOrDefault(n => n.AgentName == dep);
                if (targetNode == null) continue;
                Edges.Add(new EdgeViewModel
                {
                    SourceAgentName = sourceNode.AgentName,
                    TargetAgentName = targetNode.AgentName,
                    SourceNode = sourceNode,
                    TargetNode = targetNode,
                });
            }
        }

        Status = $"已加载 {Nodes.Count} 节点 · {Edges.Count} 边";
    }

    // ===== 保存回 DB =====
    public void SaveToDb()
    {
        try
        {
            // 先全量清空所有 Agent 的 depends_on（全量重写，避免残留）
            var allAgents = AgentRepository.GetAll();
            foreach (var a in allAgents)
            {
                a.DependsOn?.Clear();
            }

            // 再按当前 Edges 重建
            foreach (var edge in Edges)
            {
                var source = allAgents.FirstOrDefault(a => a.Name == edge.SourceAgentName);
                var target = allAgents.FirstOrDefault(a => a.Name == edge.TargetAgentName);
                if (source != null && target != null)
                {
                    source.DependsOn ??= new List<string>();
                    if (!source.DependsOn.Contains(target.Name))
                        source.DependsOn.Add(target.Name);
                }
            }

            // 逐个 Update
            foreach (var a in allAgents)
            {
                AgentRepository.Update(a);
            }

            Status = $"✅ 已保存 {Edges.Count} 条依赖关系";
        }
        catch (Exception ex)
        {
            Status = $"❌ 保存失败：{ex.Message}";
        }
    }

    public void Reset()
    {
        LoadFromAgents();
    }

    public void ClearAll()
    {
        Edges.Clear();
        Status = "已清空所有边（未保存 · 点💾持久化）";
    }
}

/// <summary>
/// 画布节点 —— 对应一个 Agent
/// </summary>
public class NodeViewModel : ObservableObject
{
    public int AgentId { get; set; }
    public string AgentName { get; set; } = "";   // kebab-case（唯一键）
    public string CnName { get; set; } = "";
    public string Emoji { get; set; } = "🤖";
    public string Priority { get; set; } = "";

    public Agent? Agent { get; set; }  // 引用原始 Agent（SaveToDb 用）

    private double _x;
    public double X { get => _x; set { if (SetProperty(ref _x, value)) OnPositionChanged(); } }

    private double _y;
    public double Y { get => _y; set { if (SetProperty(ref _y, value)) OnPositionChanged(); } }

    public const double NodeWidth = 180;
    public const double NodeHeight = 60;

    public Point Center => new(X, Y);
    public Rect Bounds => new(X - NodeWidth / 2, Y - NodeHeight / 2, NodeWidth, NodeHeight);

    /// <summary>
    /// 节点中心变化时广播 → EdgeViewModel.UpdateFromNodes 响应
    /// </summarysummary>
    public event EventHandler? PositionChanged;
    private void OnPositionChanged() => PositionChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 画布边 —— 从 source → target（source 依赖 target）
/// </summary>
public class EdgeViewModel : ObservableObject
{
    public string SourceAgentName { get; set; } = "";  // kebab-case
    public string TargetAgentName { get; set; } = "";

    public NodeViewModel? SourceNode { get; set; }
    public NodeViewModel? TargetNode { get; set; }

    // 线端点坐标（节点出口/入口圆点位置）
    private Point _sourcePoint;
    public Point SourcePoint { get => _sourcePoint; set => SetProperty(ref _sourcePoint, value); }

    private Point _targetPoint;
    public Point TargetPoint { get => _targetPoint; set => SetProperty(ref _targetPoint, value); }

    public EdgeViewModel()
    {
        // 订阅节点位置变化 → 自动刷新线端点
        // （等 SourceNode/TargetNode 被赋值后再绑）
    }

    public void BindNodes()
    {
        if (SourceNode != null) SourceNode.PositionChanged += OnNodePositionChanged;
        if (TargetNode != null) TargetNode.PositionChanged += OnNodePositionChanged;
        UpdateFromNodes();
    }

    private void OnNodePositionChanged(object? sender, EventArgs e) => UpdateFromNodes();

    public void UpdateFromNodes()
    {
        if (SourceNode == null || TargetNode == null) return;

        // 从 SourceNode 右侧 → TargetNode 左侧
        SourcePoint = new Point(SourceNode.X + NodeViewModel.NodeWidth / 2, SourceNode.Y);
        TargetPoint = new Point(TargetNode.X - NodeViewModel.NodeWidth / 2, TargetNode.Y);
    }
}
