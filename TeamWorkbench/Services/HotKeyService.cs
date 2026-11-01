using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TeamWorkbench.Services;

/// <summary>
/// 全局热键服务（Ctrl+Shift+A）
/// · P/Invoke RegisterHotKey / UnregisterHotKey
/// · 用隐藏 HwndSource 接收 WM_HOTKEY
/// · 注册失败 try/catch 兜底，不阻塞主功能
/// </summary>
public class HotKeyService : IDisposable
{
    // user32.dll
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // 常量
    private const int HOTKEY_ID = 1;
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_A = 0x41;

    private HwndSource? _source;
    private readonly MainWindow _mainWindow;
    private bool _registered;

    public HotKeyService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public bool Register()
    {
        try
        {
            var helper = new HwndSourceParameters("HotKeyHelper")
            {
                PositionX = 0, PositionY = 0,
                Width = 1, Height = 1,
                WindowStyle = 0,
                ExtendedWindowStyle = 0x08000000, // WS_EX_TOOLWINDOW（不在任务栏）
                UsesPerPixelOpacity = false
            };
            _source = new HwndSource(helper);
            _source.AddHook(WndProc);

            bool ok = RegisterHotKey(_source.Handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_A);
            if (ok)
            {
                _registered = true;
                return true;
            }
            else
            {
                // 注册失败（冲突/权限）→ 兜底，主功能不阻塞
                _source.Dispose();
                _source = null;
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            // 唤起主面板
            _mainWindow.Show();
            _mainWindow.Visibility = Visibility.Visible;
            _mainWindow.Activate();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Focus();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_registered && _source != null)
        {
            try { UnregisterHotKey(_source.Handle, HOTKEY_ID); } catch { }
            _registered = false;
        }
        _source?.Dispose();
        _source = null;
    }

    public void Dispose() => Unregister();
}
