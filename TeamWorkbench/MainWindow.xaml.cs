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
    /// Phase 2 新增：打开调用链画布窗口
    /// 用 ShowDialog 模态——防止在画布打开时主窗口触发事件
    /// </summary>
    private void OpenCallChain()
    {
        var win = new CallChainWindow();
        win.Owner = this;
        win.ShowDialog();
    }
}
