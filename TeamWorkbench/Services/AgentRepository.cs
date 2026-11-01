using Microsoft.Data.Sqlite;
using System.Text.Json;
using TeamWorkbench.Data;
using TeamWorkbench.Models;

namespace TeamWorkbench.Services;

/// <summary>
/// agents 表 CRUD + Summary
/// 对应 Node db.ts 的 queries 对象 + routes/agents.ts 的 SQL
/// </summary>
public static class AgentRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    // ========== 基础：Row → Agent ==========

    /// <summary>
    /// 从 SqliteDataReader 当前行映射成 Agent 对象
    /// </summary>
    private static Agent MapRow(SqliteDataReader reader)
    {
        var agent = new Agent
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            CnName = reader.GetString(2),
            Priority = reader.GetString(3),
            Emoji = reader.IsDBNull(4) ? "🤖" : reader.GetString(4),
            Desc = reader.IsDBNull(5) ? "" : reader.GetString(5),
            Role = reader.IsDBNull(6) ? "" : reader.GetString(6),
            InstallStatus = reader.IsDBNull(12) ? "not-installed" : reader.GetString(12),
            InstallDate = reader.IsDBNull(13) ? null : reader.GetString(13),
            RuntimeStatus = reader.IsDBNull(14) ? "idle" : reader.GetString(14),
            LastSeen = reader.IsDBNull(15) ? null : reader.GetString(15),
            UpdatedAt = reader.IsDBNull(16) ? null : reader.GetString(16),
        };

        // JSON 列：反序列化，null 给空对象
        void ParseJson<T>(int ordinal, out T result, T defaultValue) where T : class
        {
            if (reader.IsDBNull(ordinal)) { result = defaultValue; return; }
            var json = reader.GetString(ordinal);
            result = string.IsNullOrWhiteSpace(json)
                ? defaultValue
                : JsonSerializer.Deserialize<T>(json, JsonOpts) ?? defaultValue;
        }

        ParseJson(7, out TriggerInfo trigger, new TriggerInfo());
        agent.HowToTrigger = trigger;

        ParseJson(8, out List<string> process, []);
        agent.Process = process;

        ParseJson(9, out List<string> boundary, []);
        agent.Boundary = boundary;

        ParseJson(10, out List<string> tools, []);
        agent.Tools = tools;

        ParseJson(11, out List<string> dependsOn, []);
        agent.DependsOn = dependsOn;

        return agent;
    }

    // ========== Read ==========

    public static List<Agent> GetAll()
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM agents ORDER BY id;";

        var result = new List<Agent>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(MapRow(reader));
        return result;
    }

    public static Agent? GetByName(string name)
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM agents WHERE name = $name;";
        cmd.Parameters.AddWithValue("$name", name);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapRow(reader) : null;
    }

    public static AgentSummary GetSummary()
    {
        var all = GetAll();
        return new AgentSummary
        {
            Total = all.Count,
            Installed = all.Count(a => a.InstallStatus == "installed"),
            Partial = all.Count(a => a.InstallStatus == "partial"),
            Missing = all.Count(a => a.InstallStatus != "installed" && a.InstallStatus != "partial"),
            Busy = all.Count(a => a.RuntimeStatus == "busy"),
        };
    }

    /// <summary>
    /// 取下一个可用 ID（最大 ID + 1）
    /// </summary>
    public static int GetNextId()
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(id), 0) FROM agents;";
        var result = cmd.ExecuteScalar();
        return Convert.ToInt32(result) + 1;
    }

    // ========== Write ==========

    /// <summary>
    /// 新增 agent
    /// </summary>
    public static int Insert(Agent agent)
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agents (id, name, cn_name, priority, emoji, "desc", role,
                how_to_trigger, process, boundary, tools, depends_on, updated_at)
            VALUES ($id, $name, $cn_name, $priority, $emoji, $desc, $role,
                $trigger, $process, $boundary, $tools, $depends, datetime('now'));
            """;

        cmd.Parameters.AddWithValue("$id", agent.Id);
        cmd.Parameters.AddWithValue("$name", agent.Name);
        cmd.Parameters.AddWithValue("$cn_name", agent.CnName);
        cmd.Parameters.AddWithValue("$priority", agent.Priority);
        cmd.Parameters.AddWithValue("$emoji", agent.Emoji);
        cmd.Parameters.AddWithValue("$desc", agent.Desc);
        cmd.Parameters.AddWithValue("$role", agent.Role);
        cmd.Parameters.AddWithValue("$trigger", JsonSerializer.Serialize(agent.HowToTrigger, JsonOpts));
        cmd.Parameters.AddWithValue("$process", JsonSerializer.Serialize(agent.Process, JsonOpts));
        cmd.Parameters.AddWithValue("$boundary", JsonSerializer.Serialize(agent.Boundary, JsonOpts));
        cmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(agent.Tools, JsonOpts));
        cmd.Parameters.AddWithValue("$depends", JsonSerializer.Serialize(agent.DependsOn, JsonOpts));

        cmd.ExecuteNonQuery();
        return agent.Id;
    }

    /// <summary>
    /// 更新 agent 字段（只改非空属性）
    /// </summary>
    public static void Update(Agent agent)
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE agents SET cn_name=$cn, priority=$priority, emoji=$emoji,
                "desc"=$desc, role=$role, how_to_trigger=$trigger,
                process=$process, boundary=$boundary, tools=$tools,
                depends_on=$depends, updated_at=datetime('now')
            WHERE name=$name;
            """;

        cmd.Parameters.AddWithValue("$cn", agent.CnName);
        cmd.Parameters.AddWithValue("$priority", agent.Priority);
        cmd.Parameters.AddWithValue("$emoji", agent.Emoji);
        cmd.Parameters.AddWithValue("$desc", agent.Desc);
        cmd.Parameters.AddWithValue("$role", agent.Role);
        cmd.Parameters.AddWithValue("$trigger", JsonSerializer.Serialize(agent.HowToTrigger, JsonOpts));
        cmd.Parameters.AddWithValue("$process", JsonSerializer.Serialize(agent.Process, JsonOpts));
        cmd.Parameters.AddWithValue("$boundary", JsonSerializer.Serialize(agent.Boundary, JsonOpts));
        cmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(agent.Tools, JsonOpts));
        cmd.Parameters.AddWithValue("$depends", JsonSerializer.Serialize(agent.DependsOn, JsonOpts));
        cmd.Parameters.AddWithValue("$name", agent.Name);

        cmd.ExecuteNonQuery();
    }

    public static void Delete(string name)
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM agents WHERE name=$name;";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateInstallStatus(string name, string status, string? date = null)
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE agents SET install_status=$status, install_date=$date,
                updated_at=datetime('now') WHERE name=$name;
            """;
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$date", (object?)date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateRuntimeStatus(string name, string status)
    {
        using var conn = Database.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE agents SET runtime_status=$status, last_seen=datetime('now'),
                updated_at=datetime('now') WHERE name=$name;
            """;
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }
}
