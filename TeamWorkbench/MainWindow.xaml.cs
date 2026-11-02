using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using TeamWorkbench.Config;
using TeamWorkbench.Services;
using TeamWorkbench.ViewModels;
using TeamWorkbench.Views;

namespace TeamWorkbench;

public partial class MainWindow : Window
{
    private OrbWindow? _orbWindow;

    // ===== Day 14: Python 引擎 + SSE =====
    private PythonEngineHost? _pythonHost;
    private SseClient? _sseClient;
    private System.Threading.CancellationTokenSource? _globalCts;

    // ===== Day 19: 健康检查轮询 =====
    private System.Windows.Threading.DispatcherTimer? _healthTimer;
    private int _consecutiveFailures;

    public MainWindow()
    {
        InitializeComponent();

        // Phase 5 修复：Release self-contained single-file 模式下
        // XAML Height/Width 可能被忽略（窗口缩到内容最小尺寸 208x26）
        // 显式代码设置保证所有模式下窗口尺寸正确
        Height = 680;
        Width = 1280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        DataContext = new MainViewModel();

        // 顶栏按钮点击 → 显示/隐藏浮动球
        OrbToggleBtn.Click += (s, e) => ToggleOrb();

        // Phase 2 新增：调用链画布按钮
        CallChainBtn.Click += (s, e) => OpenCallChain();

        // Phase 3 Day 12 新增：PythonEngineHost 初始化（延迟到 OnContentRendered）
    }

    /// <summary>
    /// 视觉树构建完成后：Load DB + 订阅 VM 变化
    /// </summary>
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // Day 16: Settings Tab TextBox 初始化（从 AppConfig 持久化值）
        PythonExeBox.Text = AppConfig.PythonExe;
        PythonWorkingDirBox.Text = AppConfig.PythonWorkingDir;
        PythonPortBox.Text = AppConfig.PythonPort.ToString();

        if (DataContext is not MainViewModel vm) return;

        vm.LoadData();

        // 订阅 SelectedAgent 变化 → 同步更新浮动球状态
        vm.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SelectedAgent))
                SyncOrbStatus();
        };

        // Phase 2 + Day 14: 点 X → 藏托盘，但彻底关闭前清 Python
        Closing += async (s, args) =>
        {
            args.Cancel = true;

            // Phase 4: 关闭前取消事件订阅（托盘时 MainWindow 仍在，
            // 但用户显式关闭后如果再开新实例，旧实例不应继续订阅）
            EventBroadcaster.OnEventFired -= OnBackendEvent;

            // ===== Day 14: Python 清理 =====
            StopHealthPolling();
            if (_globalCts != null) { _globalCts.Cancel(); _globalCts.Dispose(); }
            if (_sseClient != null) { try { await _sseClient.DisposeAsync(); } catch { } }
            if (_pythonHost != null) { try { await _pythonHost.StopAsync(); } catch { } }

            Hide();
        };

        // Phase 4: 订阅 EventBroadcaster C# 事件桥
        // 后端任意路径调 Broadcast → 自动触发 WPF LoadData()
        EventBroadcaster.OnEventFired += OnBackendEvent;

        // ===== Day 14: 启动 Python 引擎（fire-and-forget，不阻塞 UI）=====
        _ = StartPythonEngineAsync();
    }

    /// <summary>
    /// Day 14：Python 引擎 + SSE 启动（后台 Task，不阻塞 UI 线程）
    /// · 启动失败不 crash（EngineStatus TextBlock 显示红色状态）
    /// · 成功后注入到 DispatchCenterView
    /// </summary>
    private async Task StartPythonEngineAsync()
    {
        string pyExe = AppConfig.PythonExe;
        string pyWorkingDir = AppConfig.PythonWorkingDir;

        _pythonHost = new PythonEngineHost(pythonExe: pyExe, workingDir: pyWorkingDir);

        // 先告诉 UI：正在启动
        Dispatcher.Invoke(() =>
        {
            if (Tab2LogOutput != null)
                Tab2LogOutput.AppendText("⏳ 正在启动 Python 引擎...\n");
        });

        try
        {
            await _pythonHost.StartAsync();

            // EngineStatus 状态更新
            Dispatcher.Invoke(() =>
            {
                if (Tab2LogOutput != null)
                    Tab2LogOutput.AppendText("✅ Python 引擎已就绪 · http://127.0.0.1:8765\n\n");
            });

            // 创建 SseClient + CTS
            _sseClient = new SseClient();
            _globalCts = new System.Threading.CancellationTokenSource();

            // 注入到 DispatchCenterView（Tab 1）+ 更新 EngineStatus
            Dispatcher.Invoke(() =>
            {
                DispatchCenterView?.SetPythonBridge(_pythonHost, _sseClient, _globalCts);
                DispatchCenterView?.SetEngineStatus("🟢 Python ✅ · 就绪");
            });

            // ===== Day 19: 启动健康检查 Timer =====
            StartHealthPolling();
        }
        catch (Exception ex)
        {
            // 不 crash — 显示错误状态让用户手动重启
            Dispatcher.Invoke(() =>
            {
                if (Tab2LogOutput != null)
                {
                    Tab2LogOutput.AppendText($"❌ Python 引擎启动失败：{ex.Message}\n");
                    Tab2LogOutput.AppendText("💡 检查 python-engine/ 目录是否正确、python.exe 是否在 PATH\n");
                    Tab2LogOutput.AppendText("⚙️ 可在「设置」Tab 手动配置并重启\n");
                }
            });
        }
    }

    // ===== Day 19: 健康检查轮询 =====

    private void StartHealthPolling()
    {
        _healthTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _healthTimer.Stop();  // 先停（可能已有实例）
        _consecutiveFailures = 0;
        _healthTimer.Tick += async (_, _) => await CheckHealthAsync();
        _healthTimer.Start();
    }

    public void StopHealthPolling() => _healthTimer?.Stop();

    private async Task CheckHealthAsync()
    {
        if (_pythonHost == null) return;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = await client.GetAsync($"http://127.0.0.1:{AppConfig.PythonPort}/health");
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var status = doc.RootElement.GetProperty("status").GetString();
                var litellmOk = doc.RootElement.TryGetProperty("litellm_ok", out var lo) && lo.GetBoolean();
                var dbOk = doc.RootElement.TryGetProperty("db_ok", out var db) && db.GetBoolean();

                _consecutiveFailures = 0;  // 成功清零

                var icon = status == "ok" && litellmOk && dbOk ? "🟢" : "🟡";
                var detail = status == "ok"
                    ? "Python ✅ · 就绪"
                    : $"Python 🟡 · degraded (db={dbOk}, litellm={litellmOk})";
                Dispatcher.Invoke(() => DispatchCenterView?.SetEngineStatus($"{icon} {detail}"));
            }
            else
            {
                _consecutiveFailures++;
                Dispatcher.Invoke(() => DispatchCenterView?.SetEngineStatus(
                    $"🔴 Python ❌ · HTTP {resp.StatusCode} ({_consecutiveFailures}/3)"));
                MaybeAlertCrash();
            }
        }
        catch
        {
            _consecutiveFailures++;
            Dispatcher.Invoke(() => DispatchCenterView?.SetEngineStatus(
                $"🔴 Python ❌ · 无连接 ({_consecutiveFailures}/3)"));
            MaybeAlertCrash();
        }
    }

    private void MaybeAlertCrash()
    {
        if (_consecutiveFailures != 3) return;  // 只在第 3 次失败弹窗
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                "Python 引擎连续 3 次健康检查失败（进程可能已崩溃）。\n" +
                "可在「设置」Tab 手动重启引擎，或关闭 WPF 重新打开。",
                "⚠️ Python 引擎异常",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private void ToggleOrb()
    {
        if (_orbWindow == null || !_orbWindow.IsVisible)
        {
            _orbWindow ??= new OrbWindow();
            SyncOrbStatus();
            _orbWindow.Show();
        }
        else
        {
            _orbWindow.Hide();
        }
    }

    private void SyncOrbStatus()
    {
        if (_orbWindow == null) return;
        if (DataContext is MainViewModel vm && vm.SelectedAgent != null)
        {
            _orbWindow.SetAgentStatus(vm.SelectedAgent.InstallStatus, vm.SelectedAgent.RuntimeStatus);
        }
    }

    /// <summary>
    /// Phase 3 新增：打开调用链画布窗口
    /// 用 ShowDialog 模态——防止在画布打开时主窗口触发事件
    /// </summary>
    private void OpenCallChain()
    {
        var win = new CallChainWindow();
        win.Owner = this;
        win.ShowDialog();
    }

    /// <summary>
    /// Phase 4 Day 18：一键导入花名册选中的 agent → 调度中心自动勾选
    /// </summary>
    private void ImportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedAgent == null)
        {
            MessageBox.Show("请先在左侧花名册选中一个专岗。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 切 Tab 1（调度中心）+ 自动勾选 + 聚焦
        var tabControl = FindName("MainTabControl") as TabControl;
        if (tabControl != null) tabControl.SelectedIndex = 1;

        DispatchCenterView?.SetAgentSelected(vm.SelectedAgent.Name, true);
        DispatchCenterView?.Focus();
    }

    /// <summary>
    /// Phase 3 新增：打开安装对话框
    /// </summary>
    private void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedAgent == null) return;

        var dlg = new InstallDialog(vm.SelectedAgent.Name)
        {
            Owner = this,
        };
        dlg.ShowDialog();

        // 关闭后刷新生效
        vm.LoadData();
    }

    /// <summary>
    /// Phase 3 新增：打开卸载对话框（复用 PowerShellRunner，自定义 uninstall 脚本路径）
    /// 先检查是否已装——未装时直接提示
    /// </summary>
    private void UninstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedAgent == null) return;
        if (vm.SelectedAgent.InstallStatus != "installed")
        {
            MessageBox.Show(
                $"{vm.SelectedAgent.CnName} 尚未安装，无需卸载。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"确定要卸载 {vm.SelectedAgent.CnName} 吗？",
            "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        // 卸载 = 直接删文件
        var agentsDir = TeamWorkbench.Config.AppConfig.AgentsDir;
        var skillsDir = TeamWorkbench.Config.AppConfig.SkillsDir;
        var agentMd = System.IO.Path.Combine(agentsDir, $"{vm.SelectedAgent.Name}.md");
        var skillDir = System.IO.Path.Combine(skillsDir, vm.SelectedAgent.Name);

        try
        {
            if (System.IO.File.Exists(agentMd)) System.IO.File.Delete(agentMd);
            if (System.IO.Directory.Exists(skillDir)) System.IO.Directory.Delete(skillDir, true);
            MessageBox.Show("✅ 卸载完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"❌ 卸载失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        vm.LoadData();
    }

    // ===== Phase 3 Day 15: Settings Tab 按钮 =====

    private async void RestartEngineBtn_Click(object sender, RoutedEventArgs e)
    {
        RestartEngineBtn.IsEnabled = false;
        try
        {
            // 1. 停旧引擎
            if (_pythonHost != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (Tab2LogOutput != null)
                        Tab2LogOutput.AppendText("⏹ 正在停止 Python 引擎...\n");
                });
                await _pythonHost.StopAsync();
            }

            // 2. 清旧资源
            if (_globalCts != null) { _globalCts.Cancel(); _globalCts.Dispose(); _globalCts = null; }
            if (_sseClient != null) { try { await _sseClient.DisposeAsync(); } catch { } _sseClient = null; }

            // 3. 后台重启
            _ = StartPythonEngineAsync();
        }
        catch (Exception ex)
        {
            if (Tab2LogOutput != null)
                Tab2LogOutput.AppendText($"❌ 重启失败: {ex.Message}\n");
        }
        finally
        {
            RestartEngineBtn.IsEnabled = true;
        }
    }

    private void SaveSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 读 TextBox → 写 AppConfig → 存 JSON
            AppConfig.PythonExe = PythonExeBox.Text?.Trim() ?? AppConfig.PythonExe;
            AppConfig.PythonWorkingDir = PythonWorkingDirBox.Text?.Trim() ?? AppConfig.PythonWorkingDir;

            if (int.TryParse(PythonPortBox.Text, out int port))
                AppConfig.PythonPort = port;

            AppConfig.Save();

            MessageBox.Show(
                $"✅ 设置已保存\n\nPython: {AppConfig.PythonExe}\nDir: {AppConfig.PythonWorkingDir}\nPort: {AppConfig.PythonPort}",
                "💾 保存成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"❌ 保存失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Phase 4: EventBroadcaster C# 事件桥 handler
    /// · 后端 HTTP API / FileInstaller / HttpServer 任意路径调 Broadcast
    /// · 触发 WPF UI 刷新 → 状态灯自动变
    /// · 关键：可能在后台线程（HTTP listener 线程）触发
    ///   → Dispatcher.CheckAccess() 判断 → Invoke 回 UI 线程
    /// </summary>
    private void OnBackendEvent(string eventType, string agentName, object? data)
    {
        if (DataContext is not MainViewModel vm) return;

        System.Diagnostics.Debug.WriteLine(
            $"📡 WS-EVENT 收到: type={eventType} agent={agentName}");

        // Dispatcher.CheckAccess() → true 表示已在 UI 线程，直接调
        // false 表示后台线程，需要 Invoke 回 UI 线程（UI 线程安全）
        if (Dispatcher.CheckAccess())
            vm.LoadData();
        else
            Dispatcher.Invoke(() => vm.LoadData());
    }
}
