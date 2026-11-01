using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TeamWorkbench.Views;

/// <summary>
/// 调用链画布上的可拖拽 Agent 卡片
/// · 外层 Border 是卡片本体（180×60，深色圆角）
/// · 内层 Thumb 覆盖整个卡片（DragDelta → 移动）
/// · 左侧 EntryDot（蓝色）— 连线命中目标
/// · 右侧 ExitDot（红色）— MouseDown 启动连线
/// </summary>
public partial class CallChainNode : System.Windows.Controls.UserControl
{
    public CallChainNode()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 暴露给 CallChainWindow：拿到 EntryDot / ExitDot 做 HitTest
    /// </summary>
    public System.Windows.Shapes.Ellipse EntryDotEllipse => EntryDot;
    public System.Windows.Shapes.Ellipse ExitDotEllipse => ExitDot;
    public Thumb DragThumbControl => DragThumb;
}
