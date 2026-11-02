using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;

namespace TeamWorkbench.Services;

/// <summary>
/// Python 引擎进程管理（精确对齐指南针 L524-605）
/// 
/// 生产模式：
///   CREATE_NEW_PROCESS_GROUP 隔离 Ctrl+C（指南针 Windows subprocess 设计）
///   StartAsync → POST /health 轮询等就绪（最多 30s）
///   StopAsync 三步：POST /shutdown → Process.Kill(false) 给时间处理 → 5s 后强杀
/// 
/// Day 11 MVP：基础版跑通 → Day 14 进程组隔离 + 进程树 kill
/// </summary>
public class PythonEngineHost : IAsyncDisposable
{
    private Process? _python;
    private readonly string _pythonExe;
    private readonly string _workingDir;
    private readonly string _healthUrl = "http://127.0.0.1:8765/health";
    private readonly string _shutdownUrl = "http://127.0.0.1:8765/shutdown";

    public bool IsRunning => _python is { HasExited: false };
    public int? ExitCode => _python?.ExitCode;

    /// <summary>
    /// ctor：PythonEngineHost 默认找 "python" + 工作目录 = python-engine/
    /// 或手动指定完整路径
    /// </summary>
    public PythonEngineHost(
        string? pythonExe = null,
        string? workingDir = null)
    {
        _pythonExe = pythonExe ?? "python";
        _workingDir = workingDir
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "python-engine");
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;

        var psi = new ProcessStartInfo
        {
            FileName = _pythonExe,
            Arguments = "-m uvicorn main:app --host 127.0.0.1 --port 8765 --no-access-log",
            WorkingDirectory = _workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNewProcessGroup = true,
        };

        _python = Process.Start(psi)
            ?? throw new InvalidOperationException("Python engine failed to start");

        // 就绪等待：最多 30s（指南针 L555-560）
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            if (_python.HasExited)
                throw new InvalidOperationException(
                    $"Python exited unexpectedly with code {_python.ExitCode}");
            if (await IsHealthyAsync()) return;
        }

        throw new InvalidOperationException("Python engine failed to start within 30s");
    }

    public async Task StopAsync()
    {
        if (_python == null) return;

        // Step 1: 优雅关闭（让 Python 保存 checkpoint）
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            await client.PostAsync(_shutdownUrl, null);
            await Task.Delay(1000);
        }
        catch { /* 端口可能已关 */ }

        // Step 2: 信号终止
        try { _python.Kill(); } catch { }

        // Step 3: 5s 后强杀进程树（Day 14 补进程组隔离后可用 Kill(true)）
        if (!_python.WaitForExit(5000))
        {
            try { _python.Kill(); } catch { }
        }

        _python.Dispose();
        _python = null;
    }

    private async Task<bool> IsHealthyAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await client.GetAsync(_healthUrl);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_python != null) await StopAsync();
        await ValueTask.CompletedTask;
    }
}
