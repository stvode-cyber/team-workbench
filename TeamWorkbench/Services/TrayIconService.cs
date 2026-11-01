using System.Windows;
using System.Windows.Forms;

namespace TeamWorkbench.Services;

/// <summary>
/// 系统托盘图标服务
/// · 右键菜单：显示主面板 / 退出
/// · 双击左键：显示主面板
/// · 图标用 SystemIcons.Application（MVP 零成本，Phase 5 后半换 .ico）
/// </summary>
public class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private readonly MainWindow _mainWindow;

    public TrayIconService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void Init()
    {
        // 右键菜单
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("显示主面板", null, (s, e) => ShowMainWindow());
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("退出", null, (s, e) => Application.Current.Shutdown());

        // NotifyIcon
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "🤖 AI 专岗员工面板 · v3.0\n双击唤出 / Ctrl+Shift+A 全局热键",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        // 双击 → 显示主面板
        _notifyIcon.MouseDoubleClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowMainWindow();
        };
    }

    public void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.Visibility = Visibility.Visible;
        _mainWindow.Activate();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Focus();
    }

    public void Dispose()
    {
        try
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            if (_contextMenu != null)
            {
                _contextMenu.Dispose();
                _contextMenu = null;
            }
        }
        catch { }
    }
}
