using System.IO;
using Microsoft.Data.Sqlite;

namespace TeamWorkbench.Data;

/// <summary>
/// SQLite DB 初始化 + schema + 幂等列级迁移
/// 对应 Node 后端 db.ts —— 列级完全镜像
/// </summary>
public static class Database
{
    public static string DbPath { get; private set; } = "";
    public const int SchemaVersion = 2;

    /// <summary>
    /// 启动初始化（默认 AppData 路径）。
    /// 幂等：DbPath 已被外部设过时（测试 fixture 场景）只跑 EnsureSchema。
    /// </summary>
    public static void Initialize()
    {
        if (string.IsNullOrEmpty(DbPath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TeamWorkbench");
            Directory.CreateDirectory(dir);
            DbPath = Path.Combine(dir, "data.db");
        }

        EnsureSchema();
    }

    /// <summary>
    /// 指定路径初始化（测试用临时文件 / :memory:）。
    /// 总是强制切 DbPath——测试 fixture 每次需要独立 temp file。
    /// </summary>
    public static void Initialize(string path)
    {
        DbPath = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !path.StartsWith(":memory:"))
            Directory.CreateDirectory(dir);

        EnsureSchema();
    }

    /// <summary>
    /// 确保 schema 已就绪（CREATE TABLE IF NOT EXISTS + PRAGMA 迁移 + 版本记录）
    /// </summary>
    private static void EnsureSchema()
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        // 幂等建表
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS agents (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                cn_name TEXT NOT NULL,
                priority TEXT NOT NULL CHECK(priority IN ('P0','P1')),
                emoji TEXT DEFAULT '🤖',
                "desc" TEXT DEFAULT '',
                role TEXT DEFAULT '',
                how_to_trigger TEXT,
                process TEXT,
                boundary TEXT,
                tools TEXT,
                depends_on TEXT,
                install_status TEXT DEFAULT 'not-installed',
                install_date TEXT,
                runtime_status TEXT DEFAULT 'idle' CHECK(runtime_status IN ('idle','busy')),
                last_seen TEXT,
                updated_at TEXT
            );

            CREATE TABLE IF NOT EXISTS install_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                agent_name TEXT NOT NULL,
                result TEXT NOT NULL CHECK(result IN ('success','failed')),
                detail TEXT,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS status_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                agent_name TEXT NOT NULL,
                event_type TEXT NOT NULL,
                event_data TEXT,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY,
                updated_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        // 列级 PRAGMA 迁移
        MigrateAgentsTable(conn);

        // 记录版本
        cmd.CommandText = $"INSERT OR REPLACE INTO schema_version VALUES ({SchemaVersion}, datetime('now'));";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// PRAGMA table_info + ALTER TABLE ADD COLUMN
    /// SQLite ALTER TABLE 限制：不能加 PRIMARY KEY / UNIQUE
    /// 我们的列全是普通 TEXT，没问题
    /// </summary>
    private static void MigrateAgentsTable(SqliteConnection conn)
    {
        // agents 表目标列清单（和 Node db.ts 完全对齐）
        string[][] targetColumns = [
            ["emoji",          "TEXT DEFAULT '🤖'"],
            ["desc",           "TEXT DEFAULT ''"],
            ["role",           "TEXT DEFAULT ''"],
            ["how_to_trigger", "TEXT"],
            ["process",        "TEXT"],
            ["boundary",       "TEXT"],
            ["tools",          "TEXT"],
            ["depends_on",     "TEXT"],
            ["install_status", "TEXT DEFAULT 'not-installed'"],
            ["install_date",   "TEXT"],
            ["runtime_status", "TEXT DEFAULT 'idle'"],
            ["last_seen",      "TEXT"],
            ["updated_at",     "TEXT"],
        ];

        // 读出现有列
        var existing = new HashSet<string>();
        using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA table_info(agents);";
            using var reader = pragmaCmd.ExecuteReader();
            while (reader.Read()) existing.Add(reader.GetString(1)); // name
        }

        foreach (var col in targetColumns)
        {
            if (!existing.Contains(col[0]))
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE agents ADD COLUMN {col[0]} {col[1]};";
                alterCmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// 拿一个新连接（调用方负责 using）
    /// </summary>
    public static SqliteConnection GetConnection()
    {
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        return conn;
    }
}
