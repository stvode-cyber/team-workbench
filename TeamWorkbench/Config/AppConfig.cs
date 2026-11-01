using System.IO;

namespace TeamWorkbench.Config;

/// <summary>
/// 应用配置（MVP 硬编码常量，Phase 5 转 appsettings.json）
/// </summary>
public static class AppConfig
{
    // HTTP
    public const int HttpPort = 3001;
    public static readonly string LocalhostPrefix = $"http://localhost:{HttpPort}/";

    // Trae 安装路径（Windows 默认）
    public static readonly string AgentsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".trae-cn", "agents");

    public static readonly string SkillsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".trae-cn", "skills");

    // 源文件根目录（02_AGENTS 三件套的来源）
    public static readonly string SourceDirRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Desktop", "Agent", "团队工作台", "专岗工作台", "02_AGENTS");

    // scripts 目录（install-single-agent.ps1 所在）
    public static readonly string ScriptsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Desktop", "Agent", "团队工作台", "scripts");

    public static string InstallSingleAgentScript =>
        Path.Combine(ScriptsDir, "install-single-agent.ps1");
}
