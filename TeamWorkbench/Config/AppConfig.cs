using System.IO;
using System.Text.Json;

namespace TeamWorkbench.Config;

/// <summary>
/// 应用配置（MVP 硬编码常量 → v3.1.0 增量加 Python 引擎可配置字段 + JSON 持久化）
/// 
/// 持久化位置：%AppData%/TeamWorkbench/config.json
/// 加载时机：App 启动早期（Load() 幂等，无文件则写默认值）
/// </summary>
public static class AppConfig
{
    // ===== HTTP（v3.0 固定常量）=====
    public const int HttpPort = 3001;
    public static readonly string LocalhostPrefix = $"http://localhost:{HttpPort}/";

    // ===== Trae 安装路径（Windows 默认，v3.0 硬编码）=====
    public static readonly string AgentsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".trae-cn", "agents");

    public static readonly string SkillsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".trae-cn", "skills");

    public static readonly string SourceDirRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Desktop", "Agent", "团队工作台", "专岗工作台", "02_AGENTS");

    public static readonly string ScriptsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Desktop", "Agent", "团队工作台", "scripts");

    public static string InstallSingleAgentScript =>
        Path.Combine(ScriptsDir, "install-single-agent.ps1");

    // ===== Day 16: Python 引擎可配置（v3.1.0 新增）=====
    // 持久化到 %AppData%/TeamWorkbench/config.json
    public static string PythonExe { get; set; } = "python";

    public static string PythonWorkingDir { get; set; } = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "python-engine");

    public static int PythonPort { get; set; } = 8765;

    // ===== 持久化基础设施 =====
    private static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TeamWorkbench");

    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    /// <summary>
    /// 幂等加载：文件不存在 → 写默认值 → 返回；存在 → 读字段覆盖默认
    /// </summary>
    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Save(); // 首次运行写默认
                return;
            }

            var json = File.ReadAllText(ConfigPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("PythonExe", out var pe)
                && pe.ValueKind == System.Text.Json.JsonValueKind.String)
                PythonExe = pe.GetString() ?? PythonExe;

            if (root.TryGetProperty("PythonWorkingDir", out var pwd)
                && pwd.ValueKind == System.Text.Json.JsonValueKind.String)
                PythonWorkingDir = pwd.GetString() ?? PythonWorkingDir;

            if (root.TryGetProperty("PythonPort", out var pp)
                && pp.ValueKind == System.Text.Json.JsonValueKind.Number)
                PythonPort = pp.GetInt32();
        }
        catch
        {
            // 配置文件损坏 → 用默认值（不 crash）
        }
    }

    /// <summary>
    /// 写 JSON 到 %AppData%/TeamWorkbench/config.json
    /// </summary>
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(new
            {
                PythonExe,
                PythonWorkingDir,
                PythonPort
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // 保存失败不 crash（配置目录权限问题等）
        }
    }
}
