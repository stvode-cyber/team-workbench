using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using TeamWorkbench.Models;
using TeamWorkbench.Services;
using TeamWorkbench.Views;

namespace TeamWorkbench.Views;

/// <summary>
/// Day 13-14：调度中心三栏 UI
/// · 左栏：Agent CheckBoxList（多选）
/// · 中栏：模式下拉 + Prompt TextBox + 开始/停止按钮
/// · 右栏：RichTextBox（Day 14 接 SseClient.OnEvent）
///
/// Day 14 新增：SetPythonBridge 注入 PythonEngineHost + SseClient + CTS
/// StartBtn_Click async → POST /api/chat/stream → SSE 实时输出
/// StopBtn_Click async → POST /api/sessions/{id}/abort
/// </summary>
public partial class DispatchCenterView : UserControl, INotifyPropertyChanged
{
    // ===== CheckBox 数据项 =====
    public class AgentCheckBoxItem : INotifyPropertyChanged
    {
        public required string DisplayName { get; init; }
        public required Agent Agent { get; init; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }
    }

    // ===== 公开绑定属性 =====
    public ObservableCollection<AgentCheckBoxItem> AgentCheckBoxes { get; } = new();

    // ===== Day 18: 一键导入公开 API（MainWindow + 花名册 → 调度中心）=====
    /// <summary>按 agent name 勾选/取消勾选对应 CheckBox</summary>
    public void SetAgentSelected(string agentName, bool selected)
    {
        foreach (var cb in AgentCheckBoxes)
        {
            if (string.Equals(cb.Agent.Name, agentName, StringComparison.OrdinalIgnoreCase))
            {
                cb.IsSelected = selected;
                var status = selected ? "勾选" : "取消";
                LogOutput($"✅ {status} {cb.DisplayName}");
                return;
            }
        }
        LogOutput($"⚠️ 找不到 agent: {agentName}");
    }

    /// <summary>全选</summary>
    public void SelectAllAgents()
    {
        foreach (var cb in AgentCheckBoxes) cb.IsSelected = true;
        LogOutput($"✅ 全选 {AgentCheckBoxes.Count} 个专岗");
    }

    /// <summary>清空</summary>
    public void ClearAllAgents()
    {
        foreach (var cb in AgentCheckBoxes) cb.IsSelected = false;
        LogOutput($"✅ 清空所有专岗选择");
    }

    // ===== Day 14: Python 桥字段（由 MainWindow 注入）=====
    private PythonEngineHost? _host;
    private SseClient? _sseClient;
    private CancellationTokenSource? _globalCts;
    private CancellationTokenSource? _runCts;       // 单次运行的 CTS
    private string? _currentSessionId;

    // ===== Day 16: HITL Approval 串行队列 =====
    // 多个 approval_pending 同时到达时排队，一个关了才开下一个
    private readonly Queue<(int approvalId, string agentName, string? description)> _approvalQueue = new();
    private bool _isApprovalDialogOpen;

    // ===== 构造 =====
    public DispatchCenterView()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += DispatchCenterView_Loaded;
    }

    private void DispatchCenterView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 确保 DB 初始化（安全幂等）
            Data.Database.Initialize();
            SeedService.SeedIfEmpty();

            var agents = AgentRepository.GetAll();
            AgentCheckBoxes.Clear();
            foreach (var a in agents)
            {
                AgentCheckBoxes.Add(new AgentCheckBoxItem
                {
                    DisplayName = $"{a.Emoji} {a.CnName}",
                    Agent = a,
                    IsSelected = false  // 默认不选
                });
            }

            AgentChecklist.ItemsSource = AgentCheckBoxes;
            LogOutput($"✅ 已加载 {agents.Count} 个专岗");
            LogOutput("⏳ 等待 Python 引擎就绪（启动后自动注入桥接）\n");
        }
        catch (Exception ex)
        {
            LogOutput($"❌ 加载失败: {ex.Message}");
        }
    }

    // ===== Day 18: 左栏全选/清空快捷按钮 =====
    private void SelectAllBtn_Click(object sender, RoutedEventArgs e) => SelectAllAgents();
    private void ClearAllBtn_Click(object sender, RoutedEventArgs e) => ClearAllAgents();

    // ===== Day 14: Python 桥接注入（MainWindow 调）=====
    public void SetPythonBridge(PythonEngineHost host, SseClient sseClient, CancellationTokenSource globalCts)
    {
        _host = host;
        _sseClient = sseClient;
        _globalCts = globalCts;

        LogOutput("🔗 Python 引擎桥接已就绪");
        LogOutput("💡 选择 agent → 选调度模式 → 填 prompt → 点 ▶ 开始\n");
    }

    // ===== Day 14: 状态指示更新 =====
    public void SetEngineStatus(string status)
    {
        EngineStatus.Text = status;
    }

    // ===== Day 14: 开始执行（async void 允许 await + WPF 事件 handler）=====
    private async void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        // 前置检查
        var selected = AgentCheckBoxes.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先在左栏选择至少一个专岗。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_sseClient == null)
        {
            LogOutput("❌ Python 引擎未就绪，无法执行\n");
            return;
        }

        var mode = (ModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? "HYBRID";
        // mode ComboBoxItem Content 可能是 "DIRECT · 单直调" → 截空格取第一段
        mode = mode.Split(' ')[0].Trim();

        var prompt = PromptBox.Text;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            MessageBox.Show("请填写任务 Prompt。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var agentNames = selected.Select(s => s.Agent.Name).ToArray();

        LogOutput($"\n══════════════════════════════════════════════");
        LogOutput($"▶ 执行请求");
        LogOutput($"   模式: {mode}");
        LogOutput($"   Agent: {string.Join(", ", agentNames)}");
        LogOutput($"   Prompt: {(prompt.Length > 80 ? prompt[..80] + "..." : prompt)}");
        LogOutput($"══════════════════════════════════════════════\n");

        // 构建 JSON 请求体（对齐 main.py ChatStreamRequest）
        var requestBody = new
        {
            mode = mode,
            agent_names = agentNames,
            prompt = prompt
        };
        using var jsonContent = JsonContent.Create(requestBody);

        // 创建单次运行 CTS（允许 StopBtn 单独取消）
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(
            _globalCts?.Token ?? CancellationToken.None);

        try
        {
            // ===== 订阅 SSE 事件 → 右栏 RichTextBox =====
            _sseClient.OnEvent += OnSseEvent;
            _sseClient.OnHeartbeat += OnSseHeartbeat;

            // 连接 SSE（POST /api/chat/stream）
            await _sseClient.ConnectAsync(
                new Uri("http://127.0.0.1:8765/api/chat/stream"),
                jsonContent,
                _runCts.Token);
        }
        catch (OperationCanceledException)
        {
            LogOutput("⏹ 执行已取消\n");
        }
        catch (HttpRequestException ex)
        {
            LogOutput($"❌ HTTP 错误: {ex.Message}");
            LogOutput("💡 Python 引擎可能未启动，请在 MainWindow 启动后重试\n");
        }
        catch (Exception ex)
        {
            LogOutput($"❌ 执行失败: {ex.Message}\n");
        }
        finally
        {
            // 取消订阅（避免下一次执行重复追加）
            if (_sseClient != null)
            {
                _sseClient.OnEvent -= OnSseEvent;
                _sseClient.OnHeartbeat -= OnSseHeartbeat;
            }
            _runCts?.Dispose();
            _runCts = null;
            LogOutput("══════════════════════════════════════════════\n");
        }
    }

    // ===== Day 14: 停止执行 =====
    private async void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        LogOutput("\n⏹ 停止请求...");

        // 1. 先调 Python abort endpoint（如果知道 session_id）
        if (!string.IsNullOrEmpty(_currentSessionId))
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var resp = await client.PostAsync(
                    $"http://127.0.0.1:8765/api/sessions/{_currentSessionId}/abort",
                    null);
                LogOutput($"   POST /abort → {(resp.IsSuccessStatusCode ? "✅ 已送达" : "⚠️ 未送达")}");
            }
            catch (Exception ex)
            {
                LogOutput($"   POST /abort → ⚠️ {ex.Message}");
            }
        }

        // 2. 取消本地 SSE 连接
        _runCts?.Cancel();
        _currentSessionId = null;
    }

    // ===== Day 14: SSE 事件处理（可能在后台线程 → Dispatcher 回 UI）=====
    private void OnSseEvent(object? sender, SseEvent sse)
    {
        Dispatcher.Invoke(() =>
        {
            var json = sse.Data ?? "{}";
            string prettyText;

            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 提取 session_id（第一次 run_started 时存）
                if (sse.EventType == "run_started" &&
                    root.TryGetProperty("session_id", out var sid))
                {
                    _currentSessionId = sid.GetString();
                }

                // 先提取各通用字段，避免三元内插冲突
                string? GetStr(string name)
                    => root.TryGetProperty(name, out var v)
                       && v.ValueKind == System.Text.Json.JsonValueKind.String
                       ? v.GetString() : null;

                // Day 16: approval_pending → ApprovalDialog + 串行队列 + POST respond
                bool approvalHandled = false;
                if (sse.EventType == "approval_pending" &&
                    root.TryGetProperty("approval_id", out var approvalIdElem) &&
                    approvalIdElem.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    approvalHandled = true;
                    int approvalId = approvalIdElem.GetInt32();
                    string agentName = GetStr("agent_name") ?? "unknown";
                    string? description = GetStr("task_description") ?? GetStr("prompt");

                    LogOutput($"  🛑 等待审批 agent={agentName}（approval_id={approvalId}）");

                    // 入队 + 尝试开对话框（BeginInvoke 让当前 DispatcherFrame 先跑完）
                    _approvalQueue.Enqueue((approvalId, agentName, description));
                    if (!_isApprovalDialogOpen)
                    {
                        _isApprovalDialogOpen = true;
                        Dispatcher.BeginInvoke(new Action(ProcessApprovalQueue));
                    }
                }

                prettyText = approvalHandled ? string.Empty : sse.EventType switch
                {
                    "run_started" =>
                        $"🚀 执行开始 · session={(_currentSessionId ?? "?")} · mode={GetStr("mode") ?? "?"}",
                    "agent_started" =>
                        $"  ├─ ⚡ 启动 agent={GetStr("agent_name") ?? "?"}",
                    "agent_completed" =>
                        $"  ├─ ✅ 完成 agent={GetStr("agent_name") ?? "?"}",
                    "agent_skipped" =>
                        $"  ├─ ⏭ 跳过 agent={GetStr("agent_name") ?? "?"}",
                    "session_complete" =>
                        $"🏁 执行完成 · {GetStr("summary") ?? ""}",
                    "aborted" =>
                        $"⏹ 已中断 · reason={GetStr("reason") ?? "?"}",
                    "error" =>
                        $"❌ 错误 · {GetStr("message") ?? "?"}",
                    _ => $"📡 [{sse.EventType}] {json}"
                };

                // 失败轨道错误：完整 JSON 输出方便排查
                if (sse.EventType == "error")
                    prettyText += $"\n   完整数据: {json}";
            }
            catch
            {
                prettyText = $"📡 [{sse.EventType}] {json}";
            }

            LogOutput(prettyText);
        });
    }

    private void OnSseHeartbeat(object? sender, EventArgs e)
    {
        // 心跳只更新状态，不刷日志
        Dispatcher.Invoke(() =>
        {
            EngineStatus.Text = "🟢 Python ✅ · 执行中（心跳 OK）";
        });
    }

    /// <summary>
    /// Day 16: Approval 串行队列处理
    /// · 每次取队头一个 approval，ShowDialog 模态打开
    /// · 关了之后 POST respond → 继续取下一个（递归）
    /// · 队列空了 → _isApprovalDialogOpen = false，等新的入队触发
    /// </summary>
    private void ProcessApprovalQueue()
    {
        if (_approvalQueue.Count == 0)
        {
            _isApprovalDialogOpen = false;
            return;
        }

        var (approvalId, agentName, description) = _approvalQueue.Dequeue();

        // 构造对话框
        var dlg = new ApprovalDialog(approvalId, agentName, description)
        {
            Owner = Window.GetWindow(this) // 居中于主窗口
        };

        // ShowDialog 模态 — 阻塞到用户点按钮
        var result = dlg.ShowDialog();
        bool approved = result == true;

        LogOutput($"  📤 审批结果: {agentName} → {(approved ? "✅ approved" : "❌ rejected")}");

        // POST /api/approvals/{id}/respond
        _ = Task.Run(async () =>
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var body = JsonContent.Create(new { approved, approver = "C#-WPF" });
                var resp = await client.PostAsync(
                    $"http://127.0.0.1:8765/api/approvals/{approvalId}/respond", body);
                LogOutput($"     POST /respond → {(resp.IsSuccessStatusCode ? "✅ OK" : $"⚠️ {(int)resp.StatusCode}")}");
            }
            catch (HttpRequestException ex)
            {
                LogOutput($"     POST /respond → ⚠️ {ex.Message}");
            }
        });

        // 继续取下一个（Dispatcher.BeginInvoke 让当前帧先退出）
        if (_approvalQueue.Count > 0)
        {
            Dispatcher.BeginInvoke(new Action(ProcessApprovalQueue));
        }
        else
        {
            _isApprovalDialogOpen = false;
        }
    }

    // ===== 右栏日志输出 =====
    private void LogOutput(string message)
    {
        OutputBox.AppendText(message + "\n");
        OutputBox.ScrollToEnd();
    }

    // ===== INotifyPropertyChanged（预留，Day 14 扩展）=====
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
