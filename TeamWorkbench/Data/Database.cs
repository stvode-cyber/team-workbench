using System.IO;
using Microsoft.Data.Sqlite;

namespace TeamWorkbench.Data;

/// <summary>
/// Phase 1: SQLite DB 初始化 + 3 表 schema
/// 对应后端 Node 版本的 db.ts 幂等建表
/// </summary>
public static class Database
{
    public static string DbPath { get; private set; } = "";

    public static void Initialize()
    {
        // DB 位置：%APPDATA%\TeamWorkbench\data.db
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "TeamWorkbench");
        Directory.CreateDirectory(dir);
        DbPath = Path.Combine(dir, "data.db");

        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        // 3 张表（幂等建表，CREATE TABLE IF NOT EXISTS）
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS agents (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                cn_name TEXT NOT NULL,
                priority TEXT NOT NULL CHECK(priority IN ('P0','P1')),
                emoji TEXT,
                desc TEXT,
                role TEXT,
                how_to_trigger_manual TEXT,
                how_to_trigger_auto TEXT,
                process TEXT,
                boundary TEXT,
                tools TEXT,
                install_status TEXT DEFAULT 'not-installed',
                install_date TEXT,
                runtime_status TEXT DEFAULT 'idle' CHECK(runtime_status IN ('idle','busy')),
                last_seen TEXT,
                depends_on TEXT,
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

        // 记录版本
        cmd.CommandText = "INSERT OR REPLACE INTO schema_version VALUES (1, datetime('now'));";
        cmd.ExecuteNonQuery();
    }

    public static SqliteConnection GetConnection()
    {
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        return conn;
    }
}
