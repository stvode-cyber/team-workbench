using Microsoft.Data.Sqlite;
using System.Text.Json;
using TeamWorkbench.Data;
using TeamWorkbench.Models;

namespace TeamWorkbench.Services;

/// <summary>
/// Seed 11 个专岗 agent 到 SQLite
/// 数据源：Node 后端 server/src/db.ts 的 seed 数组（第 112-212 行）
/// 策略：INSERT OR IGNORE —— 已存在的不动（保留 install_status / install_date）
/// </summary>
public static class SeedService
{
    /// <summary>
    /// 仅当 agents 表为空时才 seed（全新 DB 首次启动路径）
    /// </summary>
    public static (int inserted, int skipped) SeedIfEmpty()
    {
        using var conn = Database.GetConnection();
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM agents;";
        var count = Convert.ToInt32(countCmd.ExecuteScalar());

        if (count > 0) return (0, count);

        return InsertAll(conn);
    }

    /// <summary>
    /// 强制 seed（不管现有数据）—— 用 INSERT OR IGNORE，不覆盖已有行
    /// </summary>
    public static (int inserted, int skipped) InsertAll()
    {
        using var conn = Database.GetConnection();
        return InsertAll(conn);
    }

    private static (int inserted, int skipped) InsertAll(SqliteConnection conn)
    {
        int inserted = 0;
        int skipped = 0;

        foreach (var agent in GetSeedAgents())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO agents
                    (id, name, cn_name, priority, emoji, "desc", role,
                     how_to_trigger, process, boundary, tools, depends_on, updated_at)
                VALUES
                    ($id, $name, $cn_name, $priority, $emoji, $desc, $role,
                     $trigger, $process, $boundary, $tools, $depends, datetime('now'));
                """;

            cmd.Parameters.AddWithValue("$id", agent.Id);
            cmd.Parameters.AddWithValue("$name", agent.Name);
            cmd.Parameters.AddWithValue("$cn_name", agent.CnName);
            cmd.Parameters.AddWithValue("$priority", agent.Priority);
            cmd.Parameters.AddWithValue("$emoji", agent.Emoji);
            cmd.Parameters.AddWithValue("$desc", agent.Desc);
            cmd.Parameters.AddWithValue("$role", agent.Role);
            cmd.Parameters.AddWithValue("$trigger", JsonSerializer.Serialize(agent.HowToTrigger));
            cmd.Parameters.AddWithValue("$process", JsonSerializer.Serialize(agent.Process));
            cmd.Parameters.AddWithValue("$boundary", JsonSerializer.Serialize(agent.Boundary));
            cmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(agent.Tools));
            cmd.Parameters.AddWithValue("$depends", JsonSerializer.Serialize(agent.DependsOn));

            var changed = cmd.ExecuteNonQuery();
            if (changed > 0) inserted++; else skipped++;
        }

        return (inserted, skipped);
    }

    /// <summary>
    /// 11 个专岗 agent 的 seed 数据
    /// 从 Node db.ts seed 数组（v3 交付版本）逐行移植
    /// </summary>
    private static List<Agent> GetSeedAgents() =>
    [
        new Agent
        {
            Id = 1, Name = "drize-engineer", CnName = "推演引擎", Priority = "P0", Emoji = "🧠",
            Desc = "改代码/花钱/不可逆前必调",
            Role = "按 1号指令铁律 #22-32 执行全链路推演。在动手前证明：动作合理、有效、现实撑得住。",
            HowToTrigger = new TriggerInfo
            {
                Manual = ["推演一下", "对比方案", "正反推", "值不值得做"],
                Auto = ["改 ≥3 行代码自动触发", "花钱/上传/发布前自动触发"],
            },
            Process = [
                "先正推照这个做法走会发生什么",
                "再反推假设已经失败往回找死因",
                "失败轨道：依赖点+边界点+随机",
                "竞品五张牌：联网拉 5 个实据",
                "出推演卡：结论+回滚点+GAP 回流",
            ],
            Boundary = ["不许只推顺风局", "不许编造竞品数据", "不许跳推演直接开干"],
            Tools = ["read", "search", "web_search", "shell"],
            DependsOn = ["competitor-surveyor"],
        },
        new Agent
        {
            Id = 2, Name = "arch-designer", CnName = "架构师", Priority = "P0", Emoji = "🏗️",
            Desc = "新项目/大重构必调",
            Role = "从 01 基础设施层 → 02 平台层 → 03 应用层设计整体架构。目标：模块边界清晰、依赖方向正确。",
            HowToTrigger = new TriggerInfo
            {
                Manual = ["设计架构", "技术选型", "架构图"],
                Auto = ["新项目初始化自动触发"],
            },
            Process = [
                "扫现有代码/README/AGENTS.md",
                "确定技术栈对齐约束",
                "画 01→02→03 层架构",
                "联网查竞品架构",
                "输出架构卡",
            ],
            Boundary = ["不许选 AGENTS 禁止的技术栈", "不许跳过模块边界", "不许只给结论不给依据"],
            Tools = ["read", "search", "web_search", "shell"],
            DependsOn = ["project-taker"],
        },
        new Agent
        {
            Id = 3, Name = "project-taker", CnName = "接手员", Priority = "P0", Emoji = "🤝",
            Desc = "新接手项目必调",
            Role = "快速搞懂陌生项目并产出开工包。目标：消除「接手恐惧」——下一个 AI 不用再走一遍摸索。",
            HowToTrigger = new TriggerInfo
            {
                Manual = ["帮我接手", "摸家底", "扫一下"],
                Auto = ["刚打开新目录自动触发"],
            },
            Process = [
                "扫目录+package.json+README",
                "真跑一遍构建/测试",
                "查 git log 看最近在改什么",
                "看有没有 AGENTS.md",
                "输出项目画像",
            ],
            Boundary = ["不许瞎猜技术栈，必须真跑验证", "不许漏 AGENTS.md 检查"],
            Tools = ["read", "search", "shell"],
            DependsOn = ["agents-builder"],
        },
        new Agent
        {
            Id = 4, Name = "ledger-keeper", CnName = "台账登记员", Priority = "P0", Emoji = "📒",
            Desc = "开工/动手/收尾自动登记",
            Role = "保证所有操作有记录、每条记录有编号、编号不重复、可随时回溯。铁律：聊天里说过没登记的 = 等于没发生过。",
            HowToTrigger = new TriggerInfo
            {
                Manual = ["记台账", "登记", "编号"],
                Auto = ["Hook：开工/动手/收尾自动调"],
            },
            Process = [
                "分配下一个可用台账编号",
                "记录谁+干了啥+时间+输出在哪",
                "写入 CHANGELOG.md 或对话卡",
                "检查遗漏",
            ],
            Boundary = ["不许编造台账编号", "不许漏开工/动手/收尾三处"],
            Tools = ["read", "search", "edit"],
            DependsOn = [],
        },
        new Agent
        {
            Id = 5, Name = "rca-analyst", CnName = "RCA 根因分析师", Priority = "P1", Emoji = "🔍",
            Desc = "出 bug 后五步根因→吸收",
            Role = "根因不许停在「人为错误」。每个 RCA 必须产出至少一条回归测试 RT-xx。",
            HowToTrigger = new TriggerInfo
            {
                Manual = ["分析 bug", "根因", "RCA"],
                Auto = ["Hook：测试失败自动调"],
            },
            Process = [
                "定位现象→复现→找根因（5 Why）",
                "区分代码/设计/流程漏洞",
                "吸收进 REG-xx 判落点",
                "产出 ≥1 条 RT-xx",
                "验证修复",
            ],
            Boundary = ["不许停在「人为错误」就完了", "不许边修边找根因"],
            Tools = ["read", "search", "shell"],
            DependsOn = [],
        },
        new Agent
        {
            Id = 6, Name = "competitor-surveyor", CnName = "竞品调研员", Priority = "P1", Emoji = "🔭",
            Desc = "立项前 5 竞品实据含失败案",
            Role = "编造竞品数据比没有竞品更严重。联网拉 5 张实据，每张带来源链接和可验证数据。",
            HowToTrigger = new TriggerInfo
            {
                Manual = ["调研竞品", "行业分析", "有人做过吗"],
                Auto = ["推演引擎 Step 6 自动调"],
            },
            Process = [
                "明确调研目标",
                "联网搜 5 个竞品含失败案例",
                "记录谁+做了啥+成没成+来源",
                "分析差距",
                "输出竞品卡",
            ],
            Boundary = ["不许只列 3 个必须凑 5 个", "不许漏失败案例", "不许编造"],
            Tools = ["web_search", "read"],
            DependsOn = [],
        },
        new Agent
        {
            Id = 7, Name = "backend-expert", CnName = "后端专家", Priority = "P1", Emoji = "⚙️",
            Desc = "写 API/改 DB/加鉴权",
            Role = "铁律：先想是不是一定要写，再选最高效的办法。不要重造轮子，能复用就复用。",
            HowToTrigger = new TriggerInfo
            {
                Manual = ["写 API", "改数据库", "加鉴权", "后端"],
                Auto = ["识别到后端任务自动调"],
            },
            Process = [
                "读现有后端代码和 DB schema",
                "按 AGENTS.md 技术栈来",
                "设计接口契约",
                "安全审查",
                "输出接口设计",
            ],
            Boundary = ["不许跳过接口契约直接写实现", "不许硬编码密码/key"],
            Tools = ["read", "search", "edit", "shell"],
            DependsOn = [],
        },
        new Agent
        {
            Id = 8, Name = "safety-auditor", CnName = "安全巡视员", Priority = "P1", Emoji = "🛡️",
            Desc = "commit/发版前双检",
            Role = "安全与版本双检。核心铁律：版本号方向绝对不能反，安全红线绝对不能碰。",
            HowToTrigger = new TriggerInfo
            {
                Manual = ["检查一下", "安全审计", "扫一下"],
                Auto = ["Hook：commit/push/发版前自动调"],
            },
            Process = [
                "扫 .env / 硬编码密码",
                "查版本号是否全对齐",
                "检查别碰文件",
                "跑依赖漏洞扫描",
                "输出安全清单",
            ],
            Boundary = ["不许跳过硬编码扫描", "不许放过禁止文件改动"],
            Tools = ["read", "search", "shell"],
            DependsOn = [],
        },
        new Agent
        {
            Id = 9, Name = "report-writer", CnName = "汇报出图员", Priority = "P1", Emoji = "📊",
            Desc = "对话收尾/里程碑自动出卡",
            Role = "保证所有汇报严格遵循 report-template。铁律：全简体中文+树结构+代码块+四件套+总结。",
            HowToTrigger = new TriggerInfo
            {
                Manual = ["总结一下", "出汇报", "进度"],
                Auto = ["Hook：对话收尾自动调"],
            },
            Process = [
                "汇总三层报告",
                "更新项目进程",
                "生成 DC-xx 对话卡",
                "按模板输出",
            ],
            Boundary = ["不许漏四件套", "不许用旧格式"],
            Tools = ["read", "edit"],
            DependsOn = [],
        },
        new Agent
        {
            Id = 10, Name = "agents-builder", CnName = "AGENTS.md 构建员", Priority = "P1", Emoji = "🛠️",
            Desc = "新项目自动建 AGENTS.md",
            Role = "AGENTS 是项目设计层，只管做什么和在哪做。执行层规矩不属于 AGENTS.md。",
            HowToTrigger = new TriggerInfo
            {
                Manual = ["帮我建 AGENTS", "项目规范"],
                Auto = ["新项目无 AGENTS 自动调"],
            },
            Process = [
                "扫 package.json / 目录 / README / git log",
                "推断技术栈+项目类型",
                "生成七块区",
                "如有需要生成子目录嵌套",
                "等确认后写入",
            ],
            Boundary = ["不许编造技术栈", "生成后必须等确认"],
            Tools = ["read", "search", "shell", "edit"],
            DependsOn = [],
        },
        new Agent
        {
            Id = 11, Name = "mcp-finder", CnName = "MCP 搜索员", Priority = "P1", Emoji = "🧩",
            Desc = "联网搜 4 来源找 MCP",
            Role = "铁律：找不到就说找不到，不许瞎编。官方 awesome + Trae 社区 + npm/pip + GitHub 4 来源必查。",
            HowToTrigger = new TriggerInfo
            {
                Manual = ["有没有 MCP", "找 MCP", "装 MCP"],
                Auto = ["主 Agent 判断需要新工具时自动调"],
            },
            Process = [
                "必查 4 来源",
                "按 star≥20 + 近 3 月 commit 过滤",
                "给仓库+star+安装命令+工具",
                "找不到如实说",
            ],
            Boundary = ["不许编造 MCP", "不许推荐 star<5 或超 6 月没维护的"],
            Tools = ["web_search", "read"],
            DependsOn = [],
        },
    ];
}
