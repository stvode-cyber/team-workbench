using System.Windows;
using TeamWorkbench.Config;
using TeamWorkbench.Services;

namespace TeamWorkbench.Views;

/// <summary>
/// Agent 安装对话框
/// · RichTextBox 实时显示 PowerShell 脚本输出
/// · 跨线程安全（PowerShellRunner 已包 Dispatcher.Invoke）
/// · exit 0 → ✅ 成功 / 非 0 → ❌ 失败
/// </summary>
public partial class InstallDialog : Window
{
    private readonly PowerShellRunner _runner;
    private readonly string _agentName;
    private bool _isRunning;

    public InstallDialog(string agentName)
    {
        InitializeComponent();
        _agentName = agentName;
        _runner = new PowerShellRunner(Dispatcher);

        TitleText.Text = $"📦 安装：{agentName}";

        _runner.OnOutputLine += (s, line) => AppendOutput(line);
        _runner.OnErrorLine += (s, line) => AppendOutput(line, isError: true);
        _runner.OnExit += (s, code) => OnRunnerExit(code);

        Loaded += Dialog_Loaded;
    }

    private async void Dialog_Loaded(object sender, RoutedEventArgs e)
    {
        var scriptPath = AppConfig.InstallSingleAgentScript;
        if (!System.IO.File.Exists(scriptPath))
        {
            AppendOutput($"❌ 找不到脚本：{scriptPath}", isError: true);
            StatusText.Text = "❌ 脚本不存在";
            CloseBtn.Visibility = Visibility.Visible;
            return;
        }

        _isRunning = true;
        StopBtn.Visibility = Visibility.Visible;
        CloseBtn.Visibility = Visibility.Collapsed;
        StatusText.Text = "⏳ 运行中...";
        AppendOutput($"▶ powershell -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -AgentName \"{_agentName}\"");
        AppendOutput("");

        try
        {
            int code = await _runner.RunScriptAsync(scriptPath, "-AgentName", _agentName);
            // OnRunnerExit 会被 OnExit 事件调用，这里不用重复处理
            _ = code;
        }
        catch (Exception ex)
        {
            AppendOutput($"❌ 执行异常：{ex.Message}", isError: true);
            StatusText.Text = "❌ 执行异常";
            _isRunning = false;
            StopBtn.Visibility = Visibility.Collapsed;
            CloseBtn.Visibility = Visibility.Visible;
        }
    }

    private void AppendOutput(string line, bool isError = false)
    {
        // RichTextBox 在本 UI 线程（Dispatcher 已在 PowerShellRunner.Raise 里处理）
        var text = TerminalOutput.Document;
        var para = new System.Windows.Documents.Paragraph();
        var run = new System.Windows.Documents.Run(line)
        {
            Foreground = isError
                ? (System.Windows.Media.Brush)FindResource("StatusRedBrush")
                : (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
        };
        para.Inlines.Add(run);
        text.Blocks.Add(para);
        TerminalOutput.ScrollToEnd();
    }

    private void OnRunnerExit(int exitCode)
    {
        _isRunning = false;
        StopBtn.Visibility = Visibility.Collapsed;
        CloseBtn.Visibility = Visibility.Visible;

        if (exitCode == 0)
        {
            StatusText.Text = $"✅ 安装成功（exit 0）";
            AppendOutput("");
            AppendOutput("✅ 完成！可以关闭本窗口，状态灯将自动刷新。");
        }
        else
        {
            StatusText.Text = $"❌ 安装失败（exit {exitCode}）";
            AppendOutput("", isError: true);
            AppendOutput($"❌ 脚本返回非零 exit code: {exitCode}", isError: true);
        }
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRunning) return;
        _runner.Cancel();
        AppendOutput("⛔ 用户中断（正在终止子进程）", isError: true);
        StatusText.Text = "⛔ 已中断";
        _isRunning = false;
        StopBtn.Visibility = Visibility.Collapsed;
        CloseBtn.Visibility = Visibility.Visible;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            var result = MessageBox.Show(
                "安装还在运行中，确定关闭吗？",
                "确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            _runner.Cancel();
        }
        DialogResult = true;
        Close();
    }
}
