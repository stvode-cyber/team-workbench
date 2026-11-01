using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using TeamWorkbench.Data;
using TeamWorkbench.Models;
using TeamWorkbench.Services;

namespace TeamWorkbench.ViewModels;

/// <summary>
/// 主窗口 ViewModel
/// · ObservableCollection 存全量 AgentViewModel
/// · CollectionViewSource 做 Filter（搜索 + 状态筛选）
/// </summary>
public class MainViewModel : ObservableObject
{
    // ===== 全量 + 过滤视图 =====
    public ObservableCollection<AgentViewModel> Agents { get; } = new();
    public ICollectionView FilteredAgents { get; }

    // ===== 搜索 + 筛选 =====
    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) FilteredAgents.Refresh(); }
    }

    // 0=全部, P0, P1, installed, not-installed, busy, idle
    private string _statusFilter = "全部";
    public string StatusFilter
    {
        get => _statusFilter;
        set { if (SetProperty(ref _statusFilter, value)) FilteredAgents.Refresh(); }
    }

    public static List<string> StatusFilterOptions { get; } = new()
    {
        "全部", "P0", "P1", "已装", "待装", "忙碌", "待命"
    };

    // ===== 选中 =====
    private AgentViewModel? _selectedAgent;
    public AgentViewModel? SelectedAgent
    {
        get => _selectedAgent;
        set => SetProperty(ref _selectedAgent, value);
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

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public MainViewModel()
    {
        FilteredAgents = CollectionViewSource.GetDefaultView(Agents);
        FilteredAgents.Filter = FilterAgent;
    }

    private bool FilterAgent(object obj)
    {
        if (obj is not AgentViewModel a) return false;

        // 文本搜索：cn_name + name + role
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var s = SearchText.Trim().ToLowerInvariant();
            if (!a.CnName.ToLowerInvariant().Contains(s) &&
                !a.Name.ToLowerInvariant().Contains(s) &&
                !(a.Role?.ToLowerInvariant().Contains(s) ?? false))
                return false;
        }

        // 状态筛选
        return StatusFilter switch
        {
            "全部" => true,
            "P0" => a.Priority == "P0",
            "P1" => a.Priority == "P1",
            "已装" => a.InstallStatus == "installed",
            "待装" => a.InstallStatus != "installed",
            "忙碌" => a.RuntimeStatus == "busy",
            "待命" => a.RuntimeStatus != "busy",
            _ => true
        };
    }

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

            if (Agents.Count > 0 && SelectedAgent == null)
                SelectedAgent = Agents[0];
        }
        catch (Exception ex)
        {
            StatusText = $"❌ 加载失败: {ex.Message}";
        }
    }
}
