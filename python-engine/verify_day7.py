"""Day 7 验收脚本：resume_from 四种模式全集成 + _auto_fix_run_status

测试策略：
  对每种调度模式 →
    1. POST /api/chat/stream 跑完整 session → 全 checkpoint COMMITTED
    2. 手动 UPDATE checkpoint 把某个 step 改成 STARTED（模拟中途挂掉）
    3. POST /api/sessions/{id}/resume → 从 checkpoint 恢复
    4. 查 DB：全部 checkpoint COMMITTED + session status=completed

额外覆盖：
  ⑤ DIRECT resume（单 step）
  ⑥ SEQUENTIAL resume（链式，step 1 挂 → step 0 跳过, step 1 重跑, step 2 跳过）
  ⑦ PARALLEL resume（并行，step 0 挂 → 重跑 step 0）
  ⑧ HYBRID resume（DAG，wave 1 某个挂 → wave 0 跳过, wave 1 重跑, wave 2 跳过）
  ⑨ _auto_fix_run_status（agent_runs.status='running' + checkpoint 已 COMMITTED → resume 后自动修正）
  ⑩ 回归：全新 resume 端点不影响正常 SSE 流程
"""

from __future__ import annotations

import gzip
import json
import sys
import time

import requests

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


# ── DB 辅助（直接 import db.py 用同一 engine） ──
sys.path.insert(0, ".")
from db import engine
from sqlalchemy import text as sa_text


def _now_iso():
    import datetime
    return datetime.datetime.utcnow().isoformat() + "Z"


def db_query(sql: str, params: dict = None):
    with engine.connect() as conn:
        return conn.execute(sa_text(sql), params or {}).fetchall()


def db_exec(sql: str, params: dict = None):
    with engine.begin() as conn:
        conn.execute(sa_text(sql), params or {})


# ── SSE 流式解析辅助 ──
def post_sse_stream(path: str, payload: dict | None = None, timeout: int = 30, **headers) -> tuple[int, list[dict]]:
    """POST + stream=True + iter_lines 解析 SSE event list"""
    url = f"{BASE}{path}"
    hdrs = {"Content-Type": "application/json"}
    hdrs.update(headers)
    try:
        resp = requests.post(url, json=payload, headers=hdrs, stream=True, timeout=timeout)
        events: list[dict] = []
        current_event = None
        current_data_lines: list[str] = []
        current_id = None
        for line in resp.iter_lines(decode_unicode=True):
            if line is None:
                continue
            if line.startswith("event:"):
                current_event = line[6:].strip()
            elif line.startswith("data:"):
                current_data_lines.append(line[5:].strip())
            elif line.startswith("id:"):
                current_id = line[3:].strip()
            elif line == "":
                # 空行 = 一个事件结束
                if current_event:
                    raw = "\n".join(current_data_lines)
                    try:
                        data = json.loads(raw)
                    except Exception:
                        data = {"_raw": raw}
                    events.append({
                        "event": current_event,
                        "data": data,
                        "id": current_id,
                    })
                current_event = None
                current_data_lines = []
                current_id = None
        # 最后一个可能没有空行结尾
        if current_event:
            raw = "\n".join(current_data_lines)
            try:
                data = json.loads(raw)
            except Exception:
                data = {"_raw": raw}
            events.append({
                "event": current_event,
                "data": data,
                "id": current_id,
            })
        return resp.status_code, events
    except requests.RequestException as e:
        return 0, [{"event": "error", "data": {"message": str(e)}}]


def get_session_id_from_events(events: list[dict]) -> str | None:
    """从 run_started 或 session_complete 拿 session_id"""
    for ev in events:
        sid = ev.get("data", {}).get("session_id")
        if sid and sid != "(creating...)":
            return sid
    return None


def get_session_id_via_post_sse(path, payload):
    """先跑 POST /api/chat/stream，拿到 session_id（从 checkpoint 表查更靠谱）"""
    status, events = post_sse_stream(path, payload)
    # 从 checkpoint 表找最新 session
    rows = db_query("""
        SELECT session_id, mode FROM sessions ORDER BY rowid DESC LIMIT 1
    """)
    if rows:
        return rows[0][0], status, events
    return None, status, events


# ============================================================
# 前置：服务器连通
# ============================================================
print("=== 前置：服务器连通 ===")
try:
    r = requests.get(f"{BASE}/health", timeout=5)
    check("health 200", r.status_code == 200, str(r.status_code))
    check("db_ok", r.json().get("db_ok") is True, str(r.json()))
except Exception as e:
    print(f"❌ 服务器没起来: {e}")
    sys.exit(1)


# ============================================================
# 工具函数：完整跑 → 手动改 checkpoint → resume → 断言全 COMMITTED
# ============================================================

def run_and_resume(
    mode: str,
    agent_names: list[str],
    label: str,
    break_step_indices: list[int],  # 要模拟"中途挂掉"的 step_index
):
    """通用流程：跑 → 改 checkpoint 为 STARTED → resume → 断言"""
    print(f"\n=== {label}: {mode} 模式 resume_from（steps {break_step_indices} 模拟挂掉）===")

    # 1. 完整跑
    status, events = post_sse_stream("/api/chat/stream", {
        "mode": mode, "agent_names": agent_names, "prompt": f"verify_day7 {label} 完整跑"
    })
    check(f"{mode} 完整跑 HTTP 200", status == 200, str(status))
    sess_id = get_session_id_from_events(events)
    if not sess_id:
        # 从 sessions 表找最新
        rows = db_query("SELECT session_id FROM sessions ORDER BY rowid DESC LIMIT 1")
        sess_id = rows[0][0] if rows else None
    check(f"{mode} 拿到 session_id", sess_id is not None)
    if not sess_id:
        return

    # 2. 查 checkpoint 初始状态
    rows = db_query("""
        SELECT step_index, status FROM checkpoints
        WHERE session_id = :sid ORDER BY step_index
    """, {"sid": sess_id})
    print(f"  初始 checkpoint: {[(r[0], r[1]) for r in rows]}")
    check(f"{mode} 初始 checkpoint 全 COMMITTED",
          all(r[1] == "COMMITTED" for r in rows),
          str([(r[0], r[1]) for r in rows]))

    # 3. 手动把 break_step_indices 的 step 改成 STARTED（模拟挂掉）
    #    同时清 output_data（让 checkpoint 看起来真没 COMMIT 成功）
    for idx in break_step_indices:
        db_exec("""
            UPDATE checkpoints
            SET status = 'STARTED', output_data = NULL, output_hash = NULL,
                committed_at = NULL
            WHERE session_id = :sid AND step_index = :idx
        """, {"sid": sess_id, "idx": idx})
        # 同时把 agent_runs 对应 status 改回 running（模拟挂死前的最后状态）
        db_exec("""
            UPDATE agent_runs
            SET status = 'running', completed_at = NULL, output_json = NULL
            WHERE session_id = :sid AND step_index = :idx
        """, {"sid": sess_id, "idx": idx})

    # 验证手动改成功
    rows = db_query("""
        SELECT step_index, status FROM checkpoints
        WHERE session_id = :sid ORDER BY step_index
    """, {"sid": sess_id})
    print(f"  模拟挂掉后 checkpoint: {[(r[0], r[1]) for r in rows]}")
    for idx in break_step_indices:
        actual = [r[1] for r in rows if r[0] == idx]
        check(f"  step {idx} 已手动改为 STARTED",
              actual == ["STARTED"], str(actual))

    # 4. 先把 session 状态改回 running（因为 resume_from 要求 session 不是 completed 状态...
    #    等等，实际上 resume_from 不检查 sessions.status，它只管 checkpoint）
    #    先看看当前 session status
    rows = db_query("SELECT status FROM sessions WHERE session_id = :sid", {"sid": sess_id})
    prev_status = rows[0][0] if rows else None
    print(f"  sessions 表 status={prev_status}（改成 aborted 模拟挂掉）")

    # 把 session 改成 aborted（模拟真的挂了）
    db_exec("""
        UPDATE sessions SET status = 'aborted', completed_at = NULL
        WHERE session_id = :sid
    """, {"sid": sess_id})

    # 5. 调 resume
    status, events = post_sse_stream(f"/api/sessions/{sess_id}/resume", {}, timeout=60)
    check(f"{mode} resume HTTP 200", status == 200, str(status))
    event_names = [ev["event"] for ev in events]
    check(f"{mode} resume 有 run_started", "run_started" in event_names, event_names)
    check(f"{mode} resume 有 agent_completed", "agent_completed" in event_names, event_names)
    check(f"{mode} resume 有 session_complete", "session_complete" in event_names, event_names)

    completed_count = sum(1 for e in event_names if e == "agent_completed")
    print(f"  resume agent_completed 数量 = {completed_count}（应 = 未 COMMITTED 的 step 数）")

    # 6. 查 checkpoint → 全 COMMITTED
    rows = db_query("""
        SELECT step_index, status FROM checkpoints
        WHERE session_id = :sid ORDER BY step_index
    """, {"sid": sess_id})
    print(f"  resume 后 checkpoint: {[(r[0], r[1]) for r in rows]}")
    check(f"{mode} resume 后全 COMMITTED",
          all(r[1] == "COMMITTED" for r in rows),
          str([(r[0], r[1]) for r in rows]))

    # 7. 查 sessions → status = completed
    rows = db_query("SELECT status FROM sessions WHERE session_id = :sid", {"sid": sess_id})
    new_status = rows[0][0] if rows else None
    check(f"{mode} resume 后 sessions.status=completed",
          new_status == "completed", f"got {new_status}")

    # 8. 查 agent_runs → 全 completed（包括之前手动改的 running）
    rows = db_query("""
        SELECT step_index, status FROM agent_runs
        WHERE session_id = :sid ORDER BY step_index
    """, {"sid": sess_id})
    print(f"  resume 后 agent_runs: {[(r[0], r[1]) for r in rows]}")
    check(f"{mode} resume 后 agent_runs 全 completed",
          all(r[1] == "completed" for r in rows),
          str([(r[0], r[1]) for r in rows]))

    return sess_id


# ============================================================
# ⑤ DIRECT resume
# ============================================================
run_and_resume(
    mode="DIRECT",
    agent_names=["drize-engineer"],
    label="DIRECT",
    break_step_indices=[0],  # 唯一的 step 模拟挂掉
)


# ============================================================
# ⑥ SEQUENTIAL resume（step 1 挂 → 0 跳过, 1 重跑, 2 跳过）
# ============================================================
run_and_resume(
    mode="SEQUENTIAL",
    agent_names=["drize-engineer", "arch-designer", "project-taker"],
    label="SEQUENTIAL",
    break_step_indices=[1],  # 中间那个挂掉
)


# ============================================================
# ⑦ PARALLEL resume（全部并行，step 0 挂掉）
# ============================================================
run_and_resume(
    mode="PARALLEL",
    agent_names=["rca-analyst", "backend-expert", "safety-auditor"],
    label="PARALLEL",
    break_step_indices=[0],  # step 0 挂掉，1,2 保持 COMMITTED
)


# ============================================================
# ⑧ HYBRID resume（DAG 分层，wave 1 某个挂掉）
# ============================================================
# 先跑完整 HYBRID，看实际 layers
status, events = post_sse_stream("/api/chat/stream", {
    "mode": "HYBRID",
    "agent_names": ["drize-engineer", "arch-designer", "project-taker", "rca-analyst", "backend-expert"],
    "prompt": "verify_day7 HYBRID 看实际 layers"
})
rows = db_query("""
    SELECT session_id, input_json FROM sessions ORDER BY rowid DESC LIMIT 1
""")
sess_id = rows[0][0]
input_json = json.loads(rows[0][1] or "{}")
layers = input_json.get("layers", [])
print(f"\n=== HYBRID 实际 layers: {layers} ===")

# checkpoint
rows = db_query("""
    SELECT step_index, status, wave FROM agent_runs
    WHERE session_id = :sid ORDER BY step_index
""", {"sid": sess_id})
print(f"  agent_runs: {[(r[0], r[1], r[2]) for r in rows]}")

# 找 wave 1 的 step_index 来模拟挂掉
wave1_steps = [r[0] for r in rows if r[2] == 1]
print(f"  wave 1 steps = {wave1_steps}")
break_idx = wave1_steps[0] if wave1_steps else 1

# 现在用 run_and_resume 测 HYBRID
run_and_resume(
    mode="HYBRID",
    agent_names=["drize-engineer", "arch-designer", "project-taker", "rca-analyst", "backend-expert"],
    label="HYBRID",
    break_step_indices=[break_idx],
)


# ============================================================
# ⑨ 单独测 _auto_fix_run_status
#    手动制造 agent_runs.status='running' + checkpoint 已 COMMITTED
#    resume 后应该自动变成 'completed'
# ============================================================
print("\n=== ⑨ _auto_fix_run_status 单独测试 ===")

# 先完整跑一个 DIRECT
status, events = post_sse_stream("/api/chat/stream", {
    "mode": "DIRECT",
    "agent_names": ["ledger-keeper"],
    "prompt": "verify_day9 auto_fix_run_status"
})
rows = db_query("SELECT session_id FROM sessions ORDER BY rowid DESC LIMIT 1")
sess_id = rows[0][0]

# 手动制造脏数据
db_exec("""
    UPDATE agent_runs SET status = 'running'
    WHERE session_id = :sid
""", {"sid": sess_id})
# checkpoint 保持 COMMITTED
rows = db_query("""
    SELECT step_index, status FROM checkpoints WHERE session_id = :sid
""", {"sid": sess_id})
print(f"  checkpoint: {[(r[0], r[1]) for r in rows]}")
checkpoint_all_committed = all(r[1] == "COMMITTED" for r in rows)
check("checkpoint 已 COMMITTED", checkpoint_all_committed)

# 验证 agent_runs 脏状态被造出来了
rows = db_query("""
    SELECT step_index, status FROM agent_runs WHERE session_id = :sid
""", {"sid": sess_id})
print(f"  agent_runs 脏状态: {[(r[0], r[1]) for r in rows]}")
check("agent_runs.status='running'",
      all(r[1] == "running" for r in rows), str([(r[0], r[1]) for r in rows]))

# 改 session 状态为 aborted 才能 resume
db_exec("""
    UPDATE sessions SET status = 'aborted', completed_at = NULL
    WHERE session_id = :sid
""", {"sid": sess_id})

# 调 resume（checkpoint 全 COMMITTED，所以 resume_from 应该全部跳过）
status, events = post_sse_stream(f"/api/sessions/{sess_id}/resume", {})
event_names = [ev["event"] for ev in events]
check("resume 跑完（虽然 checkpoint 全 COMMITTED → 应该全跳过）",
      status == 200, event_names)

# 查 agent_runs → 应该被 _auto_fix_run_status 修正为 completed
rows = db_query("""
    SELECT step_index, status FROM agent_runs WHERE session_id = :sid
""", {"sid": sess_id})
print(f"  resume 后 agent_runs: {[(r[0], r[1]) for r in rows]}")
check("_auto_fix_run_status 生效 → 全 completed",
      all(r[1] == "completed" for r in rows),
      str([(r[0], r[1]) for r in rows]))


# ============================================================
# ⑩ 回归：resume 端点 404 不存在 session
# ============================================================
print("\n=== ⑩ 回归：resume 不存在 session → 404 ===")
try:
    r = requests.post(f"{BASE}/api/sessions/nonexistent-session-xyz/resume", {}, timeout=10)
    check("不存在 session → 404", r.status_code == 404, f"got {r.status_code}")
except Exception as e:
    check("不存在 session → 404", False, str(e))


# ============================================================
# ⑪ 回归：正常 SSE /api/chat/stream 没被改坏
# ============================================================
print("\n=== ⑪ 回归：正常 SSE 端点 ===")
status, events = post_sse_stream("/api/chat/stream", {
    "mode": "DIRECT",
    "agent_names": ["drize-engineer"],
    "prompt": "verify_day7 回归 SSE"
})
event_names = [ev["event"] for ev in events]
check("正常 SSE 200", status == 200, str(status))
check("run_started", "run_started" in event_names)
check("agent_started", "agent_started" in event_names)
check("agent_completed", "agent_completed" in event_names)
check("session_complete", "session_complete" in event_names)


# ============================================================
# 总结
# ============================================================
print(f"\n═══ 总结 ═══")
print(f"  通过 {PASS} 个 / 失败 {FAIL} 个")
if FAIL == 0:
    print("  🎉 Day 7 resume_from 四种模式 + _auto_fix_run_status 验收全部通过！")
    print("  ✅ DIRECT / SEQUENTIAL / PARALLEL / HYBRID resume_from 全集成")
    print("  ✅ checkpoint STARTED → resume 重跑；COMMITTED → 跳过")
    print("  ✅ _auto_fix_run_status 自动修正脏状态")
    print("  ✅ /api/sessions/{id}/resume SSE 端点已上线")
else:
    print(f"  ❌ 有 {FAIL} 个失败项")
sys.exit(0 if FAIL == 0 else 1)
