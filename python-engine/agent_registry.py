"""
Agent Registry · 双通道读 C# SQLite + agent.md

输入源：
  ① SQLite agents 表（C# 写的，11 个专岗元数据 + 状态）
  ② .trae-cn/agents/*.md 或 专岗工作台/02_AGENTS/*/agent.md（完整 system_prompt）

输出：给调度引擎的 AgentDefinition 列表（每个含 system_prompt + 所有 DB 字段）

设计（GAP-3 修正）：
  C# agents 表没有 system_prompt 列 → Python AgentRegistry 合成出来
  优先读 ~/.trae-cn/agents/（用户已安装的），备选项目目录里的源文件
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

from sqlalchemy import text

from db import engine


# ============================================================
# 数据类
# ============================================================

@dataclass
class AgentDefinition:
    """给调度引擎用的完整 Agent 定义"""
    name: str                              # drize-engineer
    cn_name: str                           # 推演引擎
    priority: str                          # P0 / P1
    role: str = ""                         # 角色定义（简要，DB 里的 role 列）
    system_prompt: str = ""                # 🔥 agent.md 全文（YAML + Markdown）
    process: list[str] = field(default_factory=list)
    boundary: list[str] = field(default_factory=list)
    tools: list[str] = field(default_factory=list)
    depends_on: list[str] = field(default_factory=list)
    install_status: str = "not-installed"
    runtime_status: str = "idle"
    emoji: str = "🤖"
    desc: str = ""
    id: int = 0
    approval_required: bool = False        # 🔥 Day 10 HITL: 执行前是否需要人类审批


# ============================================================
# AgentRegistry
# ============================================================

class AgentRegistry:
    """启动时读 C# SQLite + agent.md → 合成完整 AgentDefinition"""

    # 两个候选路径（按优先级）
    CANDIDATE_DIRS: list[Path] = []

    def __init__(self) -> None:
        # 1) 用户已安装：~/.trae-cn/agents/
        home_agents = Path.home() / ".trae-cn" / "agents"
        if home_agents.exists():
            self.CANDIDATE_DIRS.append(home_agents)

        # 2) 项目源文件：cwd/专岗工作台/02_AGENTS/*/agent.md
        project_root = Path.cwd().parent  # python-engine/ 上一级
        project_agents = project_root / "专岗工作台" / "02_AGENTS"
        if project_agents.exists():
            self.CANDIDATE_DIRS.append(project_agents)

    # ── system_prompt 读取 ──

    def _find_agent_md(self, name: str) -> Optional[Path]:
        """按候选路径找 agent.md（支持两种布局：平铺 / 序号目录）"""
        for d in self.CANDIDATE_DIRS:
            # 布局 A：~/.trae-cn/agents/drize-engineer.md
            flat = d / f"{name}.md"
            if flat.exists():
                return flat

            # 布局 B：02_AGENTS/01_drize-engineer/agent.md
            if d.name == "02_AGENTS":
                for sub in d.iterdir():
                    if sub.is_dir() and name in sub.name:
                        candidate = sub / "agent.md"
                        if candidate.exists():
                            return candidate

        return None

    def _load_system_prompt(self, name: str) -> str:
        """读 agent.md 全文（YAML frontmatter + Markdown body）"""
        md_path = self._find_agent_md(name)
        if md_path is None:
            print(f"⚠️  agent.md 找不到: {name}（搜索了 {[str(d) for d in self.CANDIDATE_DIRS]}）")
            return ""
        content = md_path.read_text(encoding="utf-8")
        print(f"  📄 {name} system_prompt: {len(content)} 字符")
        return content

    # ── JSON 列解析 ──

    @staticmethod
    def _parse_json_list(raw: Optional[str]) -> list[str]:
        """agents 表的 process/boundary/tools/depends_on 存 JSON 字符串"""
        if not raw or raw.strip() == "":
            return []
        try:
            result = json.loads(raw)
            return result if isinstance(result, list) else []
        except (json.JSONDecodeError, TypeError):
            return []

    # ── 核心：加载 ──

    # Day 10 HITL: 哪些 agent 需要人类审批（指南针 D7）
    # 硬编码等 C# agents 表加 approval_required 列后再迁移
    APPROVAL_REQUIRED_MAP: dict[str, bool] = {
        "drize-engineer": True,
        "safety-auditor": True,
    }

    def load_all(self) -> list[AgentDefinition]:
        """SELECT * FROM agents → 合成 11 个 AgentDefinition"""
        print(f"\n=== AgentRegistry.load_all() ===")
        print(f"候选 agent.md 路径: {[str(d) for d in self.CANDIDATE_DIRS]}")

        with engine.connect() as conn:
            rows = conn.execute(text(
                "SELECT id, name, cn_name, priority, emoji, \"desc\", role, "
                "       how_to_trigger, process, boundary, tools, depends_on, "
                "       install_status, install_date, runtime_status, last_seen, updated_at "
                "FROM agents ORDER BY priority, id"
            )).fetchall()

        print(f"\nSQLite agents 表: {len(rows)} 行")

        result: list[AgentDefinition] = []
        for r in rows:
            system_prompt = self._load_system_prompt(r[1])
            agent = AgentDefinition(
                id=r[0],
                name=r[1],
                cn_name=r[2],
                priority=r[3],
                emoji=r[4] or "🤖",
                desc=r[5] or "",
                role=r[6] or "",
                process=self._parse_json_list(r[8]),
                boundary=self._parse_json_list(r[9]),
                tools=self._parse_json_list(r[10]),
                depends_on=self._parse_json_list(r[11]),
                install_status=r[12] or "not-installed",
                runtime_status=r[14] or "idle",
                system_prompt=system_prompt,
                approval_required=self.APPROVAL_REQUIRED_MAP.get(r[1], False),
            )
            result.append(agent)

        installed_prompts = sum(1 for a in result if a.system_prompt)
        print(f"\n合成完成: {len(result)} 个 AgentDefinition")
        print(f"  有 system_prompt: {installed_prompts}/{len(result)}")
        print(f"  缺 system_prompt: {len(result) - installed_prompts}")
        return result

    def get(self, name: str) -> Optional[AgentDefinition]:
        """按 name 取单个（惰性：先 load_all 再 filter）"""
        all_agents = self.load_all()
        return next((a for a in all_agents if a.name == name), None)


# ============================================================
# 启动入口
# ============================================================

if __name__ == "__main__":
    registry = AgentRegistry()
    agents = registry.load_all()

    # 汇总表
    print(f"\n{'─' * 60}")
    print(f"{'ID':<3} {'PRI':<3} {'NAME':<22} {'PROMPT':<8} {'STATUS':<12}")
    print(f"{'─' * 60}")
    for a in agents:
        prompt_status = "✅" if a.system_prompt else "❌"
        print(f"{a.id:<3} {a.priority:<3} {a.name:<22} {prompt_status:<8} {a.install_status:<12}")
    print(f"{'─' * 60}")
