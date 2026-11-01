using System.ComponentModel;
using System.Runtime.CompilerServices;
using TeamWorkbench.Models;

namespace TeamWorkbench.ViewModels;

/// <summary>
/// 手动实现的 ObservableObject（零第三方依赖，等价 CommunityToolkit.Mvvm ObservableObject）
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Agent POCO → WPF 绑定 ViewModel
/// 包装 Models.Agent，暴露 ObservableProperty 供 UI 绑定
/// </summary>
public class AgentViewModel : ObservableObject
{
    private readonly Agent _agent;

    public Agent Inner => _agent;

    // ===== 基本信息 =====
    public int Id => _agent.Id;
    public string Name => _agent.Name;
    public string CnName => _agent.CnName;
    public string Priority => _agent.Priority;
    public string Emoji => _agent.Emoji;
    public string Desc => _agent.Desc;
    public string Role => _agent.Role;

    // ===== JSON 列（直接暴露，UI 直接 .Manual/.Auto 绑定）=====
    public List<string> ManualTriggers => _agent.HowToTrigger.Manual;
    public List<string> AutoTriggers => _agent.HowToTrigger.Auto;
    public List<string> Process => _agent.Process;
    public List<string> Boundary => _agent.Boundary;
    public List<string> Tools => _agent.Tools;
    public List<string> DependsOn => _agent.DependsOn;

    // ===== 状态 =====
    public string InstallStatus => _agent.InstallStatus;
    public string RuntimeStatus => _agent.RuntimeStatus;
    public string? LastSeen => _agent.LastSeen;
    public string? UpdatedAt => _agent.UpdatedAt;

    // ===== 派生属性 =====
    public string StatusLight => _agent.StatusLight;
    public string StatusLabel => _agent.StatusLabel;

    /// <summary>
    /// 中文状态色（WPF 用 SolidColorBrush）
    /// 硬编码颜色值与 Theme.xaml StatusGreen/Yellow/Red 一致
    /// </summary>
    public System.Windows.Media.Brush StatusBrush =>
        InstallStatus == "not-installed" ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F56565")) :
        RuntimeStatus == "busy" ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ECC94B")) :
        new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#48BB78"));

    /// <summary>
    /// 优先级 Chip 样式 key
    /// </summary>
    public string PriorityChipKey => _agent.Priority == "P0" ? "ChipP0" : "ChipP1";

    public AgentViewModel(Agent agent)
    {
        _agent = agent;
    }
}
