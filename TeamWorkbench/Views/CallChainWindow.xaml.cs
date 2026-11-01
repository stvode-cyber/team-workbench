using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TeamWorkbench.ViewModels;

namespace TeamWorkbench.Views;

/// <summary>
/// 调用链画布窗口
///
/// 交互：
/// · Thumb.DragDelta → 移动节点 + Edge.UpdateFromNodes() 自动刷新
/// · ExitDot MouseDown → isConnecting=true + 记录 sourceNode
/// · Canvas MouseMove（isConnecting=true）→ PreviewLine 虚线实时更新
/// · Canvas MouseUp → VisualTreeHelper.HitTest 判断命中哪个 EntryDot
///   命中 → 创建 Edge + vm.Edges.Add + source.Agent.DependsOn.Add(target.Name)
///   未命中 → isConnecting=false 放弃
///
/// 渲染：
/// · BuildCanvas() 从 vm.Nodes/vm.Edges 清空重建
/// · 每条 Edge 用 Line + 箭头 Polygon（终点旁的小三角形）
/// · 节点 CallChainNode UserControl 放到 Canvas.SetLeft/Top
/// </summary>
public partial class CallChainWindow : Window
{
    private readonly CallChainViewModel _vm;

    // 连线交互状态机
    private bool _isConnecting;
    private NodeViewModel? _connectingSourceNode;
    private Point _connectingSourceEdgePoint;  // source 节点右侧边缘起点

    public CallChainWindow()
    {
        InitializeComponent();
        _vm = new CallChainViewModel();
        DataContext = _vm;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _vm.LoadFromAgents();
        BuildCanvas();
    }

    // ==================== Canvas 构建 ====================

    private void BuildCanvas()
    {
        MainCanvas.Children.Clear();

        // ZIndex 0：边
        EdgesLayer.Children.Clear();
        foreach (var edge in _vm.Edges)
        {
            edge.BindNodes();
            AddEdgeVisual(edge);
        }
        MainCanvas.Children.Add(EdgesLayer);

        // ZIndex 1+：节点（盖在边上面）
        foreach (var node in _vm.Nodes)
        {
            AddNodeVisual(node);
        }

        UpdateStatusBar();
    }

    private void AddNodeVisual(NodeViewModel node)
    {
        var card = new CallChainNode();
        card.Width = NodeViewModel.NodeWidth;
        card.Height = NodeViewModel.NodeHeight;
        card.DataContext = node;

        // Thumb.DragDelta → 移动节点
        card.DragThumbControl.DragDelta += (s, args) =>
        {
            node.X += args.HorizontalChange;
            node.Y += args.VerticalChange;
            Canvas.SetLeft(card, node.X - NodeViewModel.NodeWidth / 2);
            Canvas.SetTop(card, node.Y - NodeViewModel.NodeHeight / 2);
            UpdateStatusBar();
        };

        // ExitDot MouseDown → 启动连线
        card.ExitDotEllipse.MouseDown += (s, args) =>
        {
            args.Handled = true;
            _isConnecting = true;
            _connectingSourceNode = node;
            _connectingSourceEdgePoint = new Point(
                node.X + NodeViewModel.NodeWidth / 2,
                node.Y);
            PreviewLine.X1 = _connectingSourceEdgePoint.X;
            PreviewLine.Y1 = _connectingSourceEdgePoint.Y;
            PreviewLine.X2 = _connectingSourceEdgePoint.X;
            PreviewLine.Y2 = _connectingSourceEdgePoint.Y;
            PreviewLine.Visibility = Visibility.Visible;
            Mouse.Capture(MainCanvas);
            UpdateStatusBar("拖拽到目标 Agent 的蓝色入口圆点松开");
        };

        // 放到 Canvas
        Canvas.SetLeft(card, node.X - NodeViewModel.NodeWidth / 2);
        Canvas.SetTop(card, node.Y - NodeViewModel.NodeHeight / 2);
        MainCanvas.Children.Add(card);
    }

    private void AddEdgeVisual(EdgeViewModel edge)
    {
        // Line
        var line = new Line
        {
            X1 = edge.SourcePoint.X,
            Y1 = edge.SourcePoint.Y,
            X2 = edge.TargetPoint.X,
            Y2 = edge.TargetPoint.Y,
            Stroke = (System.Windows.Media.Brush)FindResource("AccentBlueBrush"),
            StrokeThickness = 2,
            Tag = edge,
        };
        EdgesLayer.Children.Add(line);

        // 箭头（终点旁的小三角形）
        AddArrowHead(line);

        // 订阅 Edge 的 SourceNode/TargetNode 位置变化 → 刷新 Line
        edge.SourceNode!.PositionChanged += (_, _) => RefreshEdgeVisual(edge, line);
        edge.TargetNode!.PositionChanged += (_, _) => RefreshEdgeVisual(edge, line);
    }

    private void RefreshEdgeVisual(EdgeViewModel edge, Line line)
    {
        edge.UpdateFromNodes();
        line.X1 = edge.SourcePoint.X;
        line.Y1 = edge.SourcePoint.Y;
        line.X2 = edge.TargetPoint.X;
        line.Y2 = edge.TargetPoint.Y;
        // 刷新箭头
        var arrows = EdgesLayer.Children.OfType<Polygon>()
            .Where(p => ReferenceEquals(p.Tag, edge)).ToList();
        foreach (var a in arrows) EdgesLayer.Children.Remove(a);
        AddArrowHead(line);
    }

    private void AddArrowHead(Line line)
    {
        // 在 line 终点画一个小三角形，指向 target 方向
        double dx = line.X2 - line.X1;
        double dy = line.Y2 - line.Y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        double ux = dx / len;
        double uy = dy / len;

        double tipX = line.X2 - ux * 4;
        double tipY = line.Y2 - uy * 4;
        double baseX = line.X2 - ux * 14;
        double baseY = line.Y2 - uy * 14;
        double px = -uy;
        double py = ux;

        var arrow = new Polygon
        {
            Points = new PointCollection
            {
                new Point(tipX, tipY),
                new Point(baseX + px * 5, baseY + py * 5),
                new Point(baseX - px * 5, baseY - py * 5),
            },
            Fill = (System.Windows.Media.Brush)FindResource("AccentBlueBrush"),
            Tag = line.Tag,
        };
        EdgesLayer.Children.Add(arrow);
    }

    // ==================== 连线状态机 ====================

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isConnecting || _connectingSourceNode == null) return;
        var pos = Mouse.GetPosition(MainCanvas);
        PreviewLine.X2 = pos.X;
        PreviewLine.Y2 = pos.Y;
    }

    private void Canvas_MouseUp(object sender, RoutedEventArgs e)
    {
        if (!_isConnecting) return;
        _isConnecting = false;
        PreviewLine.Visibility = Visibility.Collapsed;
        Mouse.Capture(null);

        if (_connectingSourceNode == null) return;

        var pos = Mouse.GetPosition(MainCanvas);
        var hit = VisualTreeHelper.HitTest(MainCanvas, pos);
        var hitObj = hit?.VisualHit as DependencyObject;
        var hitEllipse = FindParentEllipse(hitObj);

        // 检查是否命中 EntryDot（且不是 ExitDot）
        bool hitEntry = hitEllipse != null &&
            hitEllipse.Tag is string tag && tag == "entry-dot";

        if (!hitEntry)
        {
            _connectingSourceNode = null;
            UpdateStatusBar("连线取消（没命中蓝色入口圆点）");
            return;
        }

        var targetNode = FindNodeByEntryDot(hitEllipse!);
        if (targetNode == null || targetNode.AgentId == _connectingSourceNode.AgentId)
        {
            _connectingSourceNode = null;
            UpdateStatusBar("连线取消（不能连到自己）");
            return;
        }

        // 去重
        bool exists = _vm.Edges.Any(ed =>
            ed.SourceAgentName == _connectingSourceNode.AgentName &&
            ed.TargetAgentName == targetNode.AgentName);
        if (exists)
        {
            _connectingSourceNode = null;
            UpdateStatusBar("连线已存在（跳过）");
            return;
        }

        // 创建 Edge + 加到 vm + 更新 EdgeVisual
        var edge = new EdgeViewModel
        {
            SourceAgentName = _connectingSourceNode.AgentName,
            TargetAgentName = targetNode.AgentName,
            SourceNode = _connectingSourceNode,
            TargetNode = targetNode,
        };
        _vm.Edges.Add(edge);

        _connectingSourceNode.Agent!.DependsOn ??= new();
        if (!_connectingSourceNode.Agent.DependsOn.Contains(targetNode.AgentName))
            _connectingSourceNode.Agent.DependsOn.Add(targetNode.AgentName);

        AddEdgeVisual(edge);
        _connectingSourceNode = null;
        UpdateStatusBar($"✅ 新连线：{_vm.Edges.Count} 条（点💾保存持久化）");
    }

    private static NodeViewModel? FindNodeByEntryDot(DependencyObject hitObj)
    {
        var el = hitObj;
        while (el != null)
        {
            if (el is CallChainNode card && card.DataContext is NodeViewModel nvm)
                return nvm;
            el = VisualTreeHelper.GetParent(el);
        }
        return null;
    }

    private static Ellipse? FindParentEllipse(DependencyObject? hitObj)
    {
        var el = hitObj;
        while (el != null)
        {
            if (el is Ellipse) return (Ellipse)el;
            el = VisualTreeHelper.GetParent(el);
        }
        return null;
    }

    // ==================== 按钮事件 ====================

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _vm.SaveToDb();
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _vm.Reset();
        BuildCanvas();
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearAll();
        BuildCanvas();
    }

    private void UpdateStatusBar(string? msg = null)
    {
        StatusBar.Text = msg ?? $"节点 {_vm.Nodes.Count} · 边 {_vm.Edges.Count}";
    }
}
