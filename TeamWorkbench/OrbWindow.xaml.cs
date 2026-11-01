using System.Windows;
using System.Windows.Input;
using WpfApp = System.Windows.Application;

namespace TeamWorkbench;

/// <summary>
/// QQ 风格浮动球窗口
/// 特性：拖拽移动 + 靠近屏幕边缘自动吸附 + Topmost + 状态徽章
/// </summary>
public partial class OrbWindow : Window
{
    private Point _dragOffset;
    private bool _isDragging;
    private const int DockThreshold = 30; // 距离边缘多少 px 内触发吸附
    private const int DockMargin = 8;     // 吸附后离边缘的距离

    public OrbWindow()
    {
        InitializeComponent();
        Loaded += OrbWindow_Loaded;
    }

    private void OrbWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 事件
        MouseLeftButtonDown += OrbWindow_MouseLeftButtonDown;
        MouseLeftButtonUp += OrbWindow_MouseLeftButtonUp;
        MouseMove += OrbWindow_MouseMove;
        LocationChanged += OrbWindow_LocationChanged;
        MouseDoubleClick += OrbWindow_MouseDoubleClick;

        // 初始状态（待命）
        SetStatus("🟢", "#48bb78");
    }

    // ========== 拖拽 ==========

    private void OrbWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragOffset = e.GetPosition(this);
        _isDragging = true;
        CaptureMouse();
    }

    private void OrbWindow_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition(this);
        var dx = pos.X - _dragOffset.X;
        var dy = pos.Y - _dragOffset.Y;
        Left += dx;
        Top += dy;
    }

    private void OrbWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
        // 松开时尝试吸屏
        CheckDock();
    }

    // ========== 吸屏 ==========

    private void OrbWindow_LocationChanged(object? sender, EventArgs e)
    {
        // 拖拽中不自动吸（只在鼠标松开时吸）
    }

    private void CheckDock()
    {
        var screen = SystemParameters.WorkArea;

        // 左右边缘检查
        double newLeft = Left;
        if (Left < DockThreshold) newLeft = DockMargin;                                 // 吸到左边
        else if (Left + Width > screen.Right - DockThreshold) newLeft = screen.Right - Width - DockMargin; // 吸到右边

        // 上下边缘检查（只吸左右，不吸上下 —— 体验更好）
        double newTop = Top;
        if (Top < DockThreshold) newTop = DockMargin;
        else if (Top + Height > screen.Bottom - DockThreshold) newTop = screen.Bottom - Height - DockMargin;

        Left = newLeft;
        Top = newTop;
    }

    // ========== 双击事件 ==========

    private void OrbWindow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 双击切换主面板显示/隐藏
        if (WpfApp.Current.MainWindow is MainWindow mw)
        {
            mw.Visibility = mw.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    // ========== 公共 API ==========

    /// <summary>
    /// 更新浮动球状态徽章（状态 emoji + 背景色）
    /// </summary>
    public void SetStatus(string emoji, string bgHex)
    {
        StatusText.Text = emoji;
        OrbBorder.Background = (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bgHex));
    }

    /// <summary>
    /// 根据 Agent 状态更新（快捷方法）
    /// </summary>
    public void SetAgentStatus(string installStatus, string runtimeStatus)
    {
        if (installStatus == "not-installed")
            SetStatus("🔴", "#f56565");
        else if (runtimeStatus == "busy")
            SetStatus("🟡", "#ecc94b");
        else
            SetStatus("🟢", "#48bb78");
    }
}
