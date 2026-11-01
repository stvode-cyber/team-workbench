using System.Collections.ObjectModel;
using TeamWorkbench.Data;
using TeamWorkbench.Models;
using TeamWorkbench.Services;

namespace TeamWorkbench.ViewModels;

/// <summary>
/// 主窗口 ViewModel
/// 持有 ObservableCollection<AgentViewModel> + SelectedAgent + Summary
/// </summary>
public class MainViewModel : ObservableObject
{
    public ObservableCollection<AgentViewModel> Agents { get; } = new();

    private AgentViewModel? _selectedAgent;
    public AgentViewModel? SelectedAgent
    {
        get => _selectedAgent;
        set => SetProperty(ref _selectedAgent, value);
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // ===== Summary =====
    private int _total;
    public int Total { get => _total; set => SetProperty(ref _total, value); }

    private int _installed;
    public int Installed { get => _installed; set => SetProperty(ref _installed, value); }

    private int _missing;
    public int Missing { get => _missing; set => SetProperty(ref _missing, value); }

    private int _busy;
    public int Busy { get => _busy; set => SetProperty(ref _busy, value); }

    /// <summary>
    /// 从 DB 读全量 + 绑定到 ObservableCollection
    /// 同步调用（WPF 启动在 UI 线程，数据量小直接同步无问题）
    /// </summary>
    public void LoadData()
    {
        try
        {
            Database.Initialize();
            SeedService.SeedIfEmpty();

            var agents = AgentRepository.GetAll();
            Agents.Clear();
            foreach (var a in agents) Agents.Add(new AgentViewModel(a));

            var summary = AgentRepository.GetSummary();
            Total = summary.Total;
            Installed = summary.Installed;
            Missing = summary.Missing;
            Busy = summary.Busy;

            StatusText = $"✅ DB就绪 · Agent: {Total} 已装:{Installed} 待装:{Missing} 忙碌:{Busy}";

            // 默认选中第一条
            if (Agents.Count > 0 && SelectedAgent == null)
                SelectedAgent = Agents[0];
        }
        catch (Exception ex)
        {
            StatusText = $"❌ 加载失败: {ex.Message}";
        }
    }
}
