using System.Diagnostics;
using System.Threading;
using System.Windows;
using TeamWorkbench.Services;

namespace TeamWorkbench;

public partial class App : Application
{
    private HttpServer? _httpServer;
    private TrayIconService? _trayIcon;
    private HotKeyService? _hotKey;
    private Mutex? _mutex;
    private MainWindow? _mainWindow;
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Phase 5: 单实例 Mutex
        _mutex = new Mutex(true, "TeamWorkbench.Mutex", out var createdNew);
        if (!createdNew)
        {
            Debug.WriteLine("⚠️ 检测到第二次启动 → 立即退出");
            // 不弹 MessageBox（无桌面/自动化场景会阻塞）
            _mutex.Dispose();
            Environment.Exit(0);
            return;
        }

        // Phase 4: 启动 HTTP Listener（后台线程，不阻塞 WPF 消息泵）
        _httpServer = new HttpServer();
        _ = _httpServer.StartAsync();

        // Phase 5: 主窗口初始化后起 Tray + HotKey
        _mainWindow = new MainWindow();
        _mainWindow.Loaded += (s, args) =>
        {
            try
            {
                _trayIcon = new TrayIconService(_mainWindow);
                _trayIcon.Init();
            }
            catch (Exception ex) { Debug.WriteLine($"⚠️ TrayIcon 初始化失败: {ex.Message}"); }

            try
            {
                _hotKey = new HotKeyService(_mainWindow);
                bool hotKeyOk = _hotKey.Register();
                Debug.WriteLine($"🚀 TeamWorkbench 启动完成 | 热键: {(hotKeyOk ? "✅" : "⚠️ 冲突")}");
            }
            catch (Exception ex) { Debug.WriteLine($"⚠️ HotKey 注册失败: {ex.Message}"); }
        };
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        Debug.WriteLine("🧹 TeamWorkbench.OnExit 清理...");

        // 清理顺序（依赖反向）：HotKey → TrayIcon → HttpServer → Mutex
        try { _hotKey?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"  HotKey.Dispose: {ex.Message}"); }
        try { _trayIcon?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"  TrayIcon.Dispose: {ex.Message}"); }
        try { _httpServer?.Stop(); } catch (Exception ex) { Debug.WriteLine($"  HttpServer.Stop: {ex.Message}"); }

        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch (Exception ex) { Debug.WriteLine($"  Mutex 清理: {ex.Message}"); }

        Debug.WriteLine("✅ 清理完成");
        base.OnExit(e);
    }
}
