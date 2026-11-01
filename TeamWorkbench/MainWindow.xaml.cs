using System.ComponentModel;
using System.Windows;
using TeamWorkbench.ViewModels;
using TeamWorkbench.Views;

namespace TeamWorkbench;

public partial class MainWindow : Window
{
    private OrbWindow? _orbWindow;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // 顶栏按钮点击 → 显示/隐藏浮动球
        OrbToggleBtn.Click += (s, e) => ToggleOrb();

        // Phase 2 新增：调用链画布按钮
        CallChainBtn.Click += (s, e) => OpenCallChain();
    }

    /// <summary>
    /// 视觉树构建完成后：Load DB + 订阅 VM 变化
    /// </summary>
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (DataContext is not MainViewModel vm) return;

        vm.LoadData();

        // 订阅 SelectedAgent 变化 → 同步更新浮动球状态
        vm.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SelectedAgent))
                SyncOrbStatus();
        };

        // Phase 5: 点 X 关闭 → 藏托盘而非退出
        Closing += (s, args) =>
        {
            args.Cancel = true;
            Hide();
        };
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
}
