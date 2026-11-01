using System.Diagnostics;
using System.Windows.Threading;

namespace TeamWorkbench.Services;

/// <summary>
/// 纯原生 PowerShell 脚本执行器
/// · Process.Start("powershell.exe -NoProfile -ExecutionPolicy Bypass -File ...")
/// · RedirectStandardOutput + RedirectStandardError
/// · 事件驱动：OnOutputLine / OnErrorLine / OnExit
/// · 跨线程安全：Dispatcher.Invoke 回到 UI 线程
/// </summary>
public class PowerShellRunner
{
    public event EventHandler<string>? OnOutputLine;
    public event EventHandler<string>? OnErrorLine;
    public event EventHandler<int>? OnExit;

    private Process? _process;
    private readonly Dispatcher? _dispatcher;

    public PowerShellRunner() : this(null) { }

    public PowerShellRunner(Dispatcher? dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// 执行 PowerShell 脚本，等待退出，返回 exit code
    /// </summary>
    public async Task<int> RunScriptAsync(string scriptPath, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = BuildArguments(scriptPath, args),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _process.OutputDataReceived += (s, e) =>
        {
            if (e.Data == null) return;
            Raise(() => OnOutputLine?.Invoke(this, e.Data));
        };
        _process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data == null) return;
            Raise(() => OnErrorLine?.Invoke(this, e.Data));
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await _process.WaitForExitAsync();

        int exitCode = _process.ExitCode;
        Raise(() => OnExit?.Invoke(this, exitCode));

        _process.Dispose();
        _process = null;

        return exitCode;
    }

    public void Cancel()
    {
        try { _process?.Kill(); } catch { }
    }

    private static string BuildArguments(string scriptPath, string[] args)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("-NoProfile -ExecutionPolicy Bypass -File ");
        sb.Append('"').Append(scriptPath).Append('"');
        foreach (var a in args) sb.Append(' ').Append(a);
        return sb.ToString();
    }

    /// <summary>
    /// 跨线程安全：有 Dispatcher 就 Invoke，否则直接触发
    /// </summary>
    private void Raise(Action action)
    {
        if (_dispatcher != null && !_dispatcher.CheckAccess())
            _dispatcher.Invoke(action);
        else
            action();
    }
}
