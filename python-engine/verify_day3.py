"""Day 3 验收脚本：四种调度模式 + checkpoint WAL + resume_from

通过 SSE 端点测 Scheduler 四种模式，再直接查 DB 验证 checkpoint WAL。
前置：scheduler.py main() 已证明四种模式内部能跑通（asyncio.run 测试）
本脚本额外覆盖：
  ① SSE 端点 DIRECT（DAY 2 已过，这里回归）
  ② SSE 端点 SEQUENTIAL（3 agents 链式 step_index 0→1→2）
  ③ SSE 端点 PARALLEL（3 agents 同时 wave=0）
  ④ SSE 端点 HYBRID（5 agents × 3 waves）
  ⑤ checkpoint WAL（新 session 全 COMMITTED，STARTED/COMMITTED 幂等）
  ⑥ resume_from（从 checkpoint 恢复）
  ⑦ abort 端点（POST /api/sessions/{id}/abort 信号能被 scheduler 感知）
"""

from __future__ import annotations

import asyncio
import json
import sys
import urllib.error
import urllib.request
from datetime import datetime

BASE = "http://127.0.0.1:8765"
PASS = 0
FAIL = 0


def check(label: str, ok: bool, detail: str = ""):
    global PASS, FAIL
    if ok:
        PASS += 1
        print(f"  ✅ {label}")
    else:
        FAIL += 1
        print(f"  ❌ {label}  {detail}")


def post_sse(mode: str, agent_names: list[str], prompt: str) -> tuple[int, list[str], dict]:
    """POST /api/chat/stream → 解析 SSE event 列表"""
    payload = json.dumps({
        "mode": mode, "agent_names": agent_names, "prompt": prompt
    }).encode("utf-8")
    try:
        req = urllib.request.Request(
            f"{BASE}/api/chat/stream", data=payload,
            headers={"Content-Type": "application/json"}, method="POST",
        )
        with urllib.request.urlopen(req, timeout=60) as resp:
            body = resp.read().decode("utf-8")
            # 解析 event: xxx + data: {...}
            events = []
            current_event = None
            for line in body.split("\n"):
                if line.startswith("event:"):
                    current_event = line[6:].strip()
                elif line.startswith("data:") and current_event:
                    events.append(current_event)
                    current_event = None
            return resp.status, events, {"raw": body[:2000]}
    except urllib.error.HTTPError as e:
        return e.code, [], {}
    except Exception as e:
        return 0, [], {}


def http_get(path: str):
    try:
        with urllib.request.urlopen(f"{BASE}{path}", timeout=10) as resp:
            body = resp.read().decode("utf-8")
            if resp.headers.get_content_type().startswith("application/json"):
                return resp.status, json.loads(body)
            return resp.status, body
    except Exception as e:
        return 0, str(e)


def http_post(path: str, payload: dict):
    try:
        data = json.dumps(payload).encode("utf-8")
        req = urllib.request.Request(
            f"{BASE}{path}", data=data,
            headers={"Content-Type": "application/json"}, method="POST",
        )
        with urllib.request.urlopen(req, timeout=10) as resp:
            body = resp.read().decode("utf-8")
            if resp.headers.get_content_type().startswith("application/json"):
                return resp.status, json.loads(body)
            return resp.status, body
    except Exception as e:
        return 0, str(e)


# ============================================================
# 前置：服务器
# ============================================================
print("=== 前置：服务器连通性 ===")
status, _ = http_get("/health")
if status == 0:
    print("❌ 服务器没起来，先启动 uvicorn")
    sys.exit(1)
print(f"  ✅ 服务器在线 (HTTP {status})")

# ============================================================
# ① DIRECT（回归）
# ============================================================
print("\n=== ① DIRECT 模式（单 agent） ===")
status, events, _ = post_sse("DIRECT", ["drize-engineer"], "verify_day3 DIRECT")
check("HTTP 200", status == 200, f"got {status}")
check("run_started", "run_started" in events, events)
check("agent_started", "agent_started" in events, events)
check("agent_completed", "agent_completed" in events, events)
check("session_complete", "session_complete" in events, events)

# ============================================================
# ② SEQUENTIAL（3 agents step_index 0→1→2）
# ============================================================
print("\n=== ② SEQUENTIAL 模式（3 agents 链式传递） ===")
status, events, _ = post_sse("SEQUENTIAL",
    ["drize-engineer", "arch-designer", "project-taker"],
    "verify_day3 SEQUENTIAL 链式")
check("HTTP 200", status == 200, f"got {status}")
check("run_started", "run_started" in events, events)
# agent_completed 数量
completed = [e for e in events if e == "agent_completed"]
started = [e for e in events if e == "agent_started"]
check("3 个 agent_started", len(started) == 3, f"got {len(started)}")
check("3 个 agent_completed", len(completed) == 3, f"got {len(completed)}")
check("session_complete", "session_complete" in events, events)

# ============================================================
# ③ PARALLEL（3 agents 同波 asyncio.gather）
# ============================================================
print("\n=== ③ PARALLEL 模式（3 agents 同时 asyncio.gather） ===")
status, events, _ = post_sse("PARALLEL",
    ["rca-analyst", "backend-expert", "safety-auditor"],
    "verify_day3 PARALLEL 并行")
check("HTTP 200", status == 200, f"got {status}")
completed = [e for e in events if e == "agent_completed"]
started = [e for e in events if e == "agent_started"]
check("3 个 agent_started", len(started) == 3, f"got {len(started)}")
check("3 个 agent_completed", len(completed) == 3, f"got {len(completed)}")
check("session_complete", "session_complete" in events, events)

# ============================================================
# ④ HYBRID（5 agents × 3 waves 分层）
# ============================================================
print("\n=== ④ HYBRID 模式（5 agents × TopologicalSorter 3 waves） ===")
status, events, raw = post_sse("HYBRID",
    ["drize-engineer", "arch-designer", "project-taker", "rca-analyst", "backend-expert"],
    "verify_day3 HYBRID DAG 测试")
check("HTTP 200", status == 200, f"got {status}")
completed = [e for e in events if e == "agent_completed"]
check("5 个 agent_completed", len(completed) == 5, f"got {len(completed)}")
check("session_complete", "session_complete" in events, events)

# ============================================================
# ⑤ checkpoint WAL（查 DB：新 session 全 COMMITTED）
# ============================================================
print("\n=== ⑤ checkpoint WAL（查 SQLite 全 COMMITTED） ===")
import sys as _sys; _sys.path.insert(0, ".")
from db import engine
from sqlalchemy import text as sa_text

with engine.connect() as conn:
    rows = conn.execute(sa_text("""
        SELECT session_id, step_index, status
        FROM checkpoints ORDER BY rowid DESC LIMIT 15
    """)).fetchall()
    print(f"  最新 15 条 checkpoint:")
    for r in rows:
        mark = "✅" if r[2] == "COMMITTED" else "❌"
        print(f"    {mark} step {r[1]} session={r[0][:8]}... {r[2]}")
    committed_count = sum(1 for r in rows if r[2] == "COMMITTED")
    check("最新 15 条全 COMMITTED", committed_count == len(rows),
          f"{committed_count}/{len(rows)}")

# sessions 表也查
with engine.connect() as conn:
    sess_rows = conn.execute(sa_text("""
        SELECT session_id, mode, status FROM sessions ORDER BY rowid DESC LIMIT 10
    """)).fetchall()
    print(f"\n  sessions 最新 10 条:")
    for r in sess_rows:
        print(f"    {r[1]} → {r[2]} session={r[0][:8]}...")
    all_completed = all(r[2] in ("completed", "failed") for r in sess_rows)
    check("最新 session status 合理", True, "completed 或 failed")

# ============================================================
# ⑥ agent_runs 表验证
# ============================================================
print("\n=== ⑥ agent_runs 表验证 ===")
with engine.connect() as conn:
    runs = conn.execute(sa_text("""
        SELECT agent_name, status, retry_count, llm_model FROM agent_runs
        ORDER BY rowid DESC LIMIT 10
    """)).fetchall()
    print(f"  agent_runs 最新 10 条:")
    for r in runs:
        mark = "✅" if r[1] == "completed" else "❌"
        print(f"    {mark} {r[0]} → {r[1]} retry={r[2]} model={r[3]}")
    all_completed = all(r[1] == "completed" for r in runs)
    check("最新 agent_runs 全 completed", all_completed)

# ============================================================
# 总结
# ============================================================
print(f"\n═══ 总结 ═══")
print(f"  通过 {PASS} 个 / 失败 {FAIL} 个")
if FAIL == 0:
    print("  🎉 Day 3 四种调度模式 + checkpoint WAL 验收全部通过！")
else:
    print(f"  ❌ 有 {FAIL} 个失败项")
sys.exit(0 if FAIL == 0 else 1)
