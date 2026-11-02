"""
Day 12: resume_from 补 HITL 验证（纯 Python，不启 uvicorn）
═══════════════════════════════════════════════════

核心改动：resume_from 里 DIRECT/SEQUENTIAL/PARALLEL 未 COMMITTED 的 step
重跑时也要过 _check_approval()（HYBRID 已有 _run_hybrid_single 自带 HITL）

测试点：
  T1. DIRECT + drize-engineer → resume → HITL 审批门被调
  T2. SEQUENTIAL 混合 COMMITTED + STARTED → resume → STARTED 步过 HITL
  T3. PARALLEL 双 STARTED → resume → 双 HITL + gather
  T4. Day 8 回归：DIRECT 全 COMMITTED → resume 直接跳过，不重跑
"""

from __future__ import annotations

import asyncio
import os
import sqlite3
import sys
import time

os.chdir(os.path.dirname(os.path.abspath(__file__)))

from sqlalchemy import text as sa_text
from db import engine as db_engine
from scheduler import SchedulerEngine, register_session, abort_session, is_aborted
from hitl import hitl

PASS = "✅ PASS"
FAIL = "❌ FAIL"


def _now_iso() -> str:
    from datetime import datetime
    return datetime.utcnow().isoformat() + "Z"


def clear_all_sessions():
    """清旧数据，避免干扰"""
    with db_engine.begin() as conn:
        conn.execute(sa_text("DELETE FROM approval_requests WHERE status='pending'"))
        conn.execute(sa_text("DELETE FROM agent_runs"))
        conn.execute(sa_text("DELETE FROM sessions"))
        conn.execute(sa_text("DELETE FROM checkpoints"))


def seed_direct_started(session_id: str):
    """模拟 DIRECT + drize-engineer STARTED（还没 COMMITTED）"""
    step_id = f"{session_id}::step0"
    with db_engine.begin() as conn:
        conn.execute(sa_text("""
            INSERT INTO sessions (session_id, mode, status, input_json, created_at)
            VALUES (:sid, 'DIRECT', 'running', '{"prompt":"test drize"}', :ts)
        """), {"sid": session_id, "ts": _now_iso()})
        conn.execute(sa_text("""
            INSERT INTO agent_runs
            (session_id, agent_name, step_index, wave, input_json, llm_provider,
             llm_model, status, idempotency_key, retry_count)
            VALUES (:sid, 'drize-engineer', 0, 0, '{}', 'openrouter', 'mock',
                    'running', :idem, 0)
        """), {"sid": session_id, "idem": step_id})
        # checkpoint STARTED（还没 COMMITTED）
        conn.execute(sa_text("""
            INSERT INTO checkpoints
            (session_id, step_id, step_index, status, input_hash, started_at, idempotency_key)
            VALUES (:sid, :step_id, 0, 'STARTED', :ih, :ts, :idem)
        """), {"sid": session_id, "step_id": step_id,
               "ih": hash("test drize"), "ts": _now_iso(), "idem": step_id})


def seed_sequential_mixed(session_id: str):
    """SEQUENTIAL: step0 arch-designer COMMITTED（不审批）, step1 drize-engineer STARTED（需审批）"""
    step0_id = f"{session_id}::step0"
    step1_id = f"{session_id}::step1"
    with db_engine.begin() as conn:
        conn.execute(sa_text("""
            INSERT INTO sessions (session_id, mode, status, input_json, created_at)
            VALUES (:sid, 'SEQUENTIAL', 'running', :ij, :ts)
        """), {"sid": session_id, "ij": '{"prompt":"seq test","agent_names":["arch-designer","drize-engineer"],"layers":null}', "ts": _now_iso()})
        # step0 arch-designer COMMITTED（不审批）
        conn.execute(sa_text("""
            INSERT INTO agent_runs
            (session_id, agent_name, step_index, wave, input_json, llm_provider,
             llm_model, status, output_json, completed_at, idempotency_key, retry_count)
            VALUES (:sid, 'arch-designer', 0, 0, '{}', 'openrouter', 'mock',
                    'completed', '{"output":"arch output"}', :ts, :idem, 0)
        """), {"sid": session_id, "idem": step0_id, "ts": _now_iso()})
        conn.execute(sa_text("""
            INSERT INTO checkpoints
            (session_id, step_id, step_index, status, input_hash, output_hash,
             committed_at, started_at, idempotency_key)
            VALUES (:sid, :step_id, 0, 'COMMITTED', :ih, :oh, :ts, :ts, :idem)
        """), {"sid": session_id, "step_id": step0_id,
               "ih": hash("prompt"), "oh": hash("arch output"),
               "ts": _now_iso(), "idem": step0_id})
        # step1 drize-engineer STARTED（需审批）
        conn.execute(sa_text("""
            INSERT INTO agent_runs
            (session_id, agent_name, step_index, wave, input_json, llm_provider,
             llm_model, status, idempotency_key, retry_count)
            VALUES (:sid, 'drize-engineer', 1, 1, '{}', 'openrouter', 'mock',
                    'running', :idem, 0)
        """), {"sid": session_id, "idem": step1_id})
        conn.execute(sa_text("""
            INSERT INTO checkpoints
            (session_id, step_id, step_index, status, input_hash, started_at, idempotency_key)
            VALUES (:sid, :step_id, 1, 'STARTED', :ih, :ts, :idem)
        """), {"sid": session_id, "step_id": step1_id,
               "ih": hash("prev: arch output"), "ts": _now_iso(), "idem": step1_id})


def seed_parallel_both_started(session_id: str):
    """PARALLEL: drize-engineer + safety-auditor 都 STARTED"""
    with db_engine.begin() as conn:
        conn.execute(sa_text("""
            INSERT INTO sessions (session_id, mode, status, input_json, created_at)
            VALUES (:sid, 'PARALLEL', 'running', :ij, :ts)
        """), {"sid": session_id, "ij": '{"prompt":"par test","agent_names":["drize-engineer","safety-auditor"],"layers":null}', "ts": _now_iso()})
        for i, name in enumerate(["drize-engineer", "safety-auditor"]):
            step_id = f"{session_id}::step{i}"
            conn.execute(sa_text("""
                INSERT INTO agent_runs
                (session_id, agent_name, step_index, wave, input_json, llm_provider,
                 llm_model, status, idempotency_key, retry_count)
                VALUES (:sid, :an, :si, 0, '{}', 'openrouter', 'mock',
                        'running', :idem, 0)
            """), {"sid": session_id, "an": name, "si": i, "idem": step_id})
            conn.execute(sa_text("""
                INSERT INTO checkpoints
                (session_id, step_id, step_index, status, input_hash, started_at, idempotency_key)
                VALUES (:sid, :step_id, :si, 'STARTED', :ih, :ts, :idem)
            """), {"sid": session_id, "step_id": step_id, "si": i,
                   "ih": hash("par test"), "ts": _now_iso(), "idem": step_id})


async def auto_approve_all(start_event: asyncio.Event) -> None:
    """后台：等 start_event 触发后，1.5s 内 approve 所有 pending"""
    await start_event.wait()
    await asyncio.sleep(0.3)
    conn = sqlite3.connect(
        os.path.join(os.environ.get("APPDATA", ""), "TeamWorkbench", "data.db")
    )
    try:
        for _ in range(5):
            rows = conn.execute(
                "SELECT id FROM approval_requests WHERE status='pending'"
            ).fetchall()
            for (rid,) in rows:
                hitl.respond(rid, True, "resume-test-auto")
            if not rows:
                await asyncio.sleep(0.3)
    finally:
        conn.close()


async def test_direct_resume_hitl():
    """T1: DIRECT + drize-engineer → STARTED → resume → HITL 被调"""
    print(f"\n{'─'*60}")
    print("T1: DIRECT resume + HITL")
    print(f"{'─'*60}")

    clear_all_sessions()
    session_id = f"t1-direct-{int(time.time())}"
    seed_direct_started(session_id)

    start = asyncio.Event()
    auto_task = asyncio.create_task(auto_approve_all(start))
    start.set()

    engine = SchedulerEngine()
    start_time = time.time()
    events, sid = await engine.resume_from(session_id)
    elapsed = time.time() - start_time

    auto_task.cancel()
    try:
        await auto_task
    except BaseException:
        pass

    pending = [e for e in events if e.event == "approval_pending"]
    completed = [e for e in events if e.event == "agent_completed"]

    print(f"  events total: {len(events)}")
    print(f"  approval_pending: {len(pending)} (应该 >= 1)")
    print(f"  agent_completed: {len(completed)}")
    print(f"  elapsed: {elapsed:.2f}s")

    ok = len(pending) >= 1 and len(completed) == 1
    print(f"  {PASS if ok else FAIL}: HITL gate {'调了' if len(pending) else '没调'}")
    return ok


async def test_sequential_resume_hitl():
    """T2: SEQUENTIAL 混合 → step1 STARTED → resume → step1 过 HITL"""
    print(f"\n{'─'*60}")
    print("T2: SEQUENTIAL resume + 混合 COMMITTED/STARTED")
    print(f"{'─'*60}")

    clear_all_sessions()
    session_id = f"t2-seq-{int(time.time())}"
    seed_sequential_mixed(session_id)

    start = asyncio.Event()
    auto_task = asyncio.create_task(auto_approve_all(start))
    start.set()

    engine = SchedulerEngine()
    events, sid = await engine.resume_from(session_id)
    await asyncio.sleep(0.5)
    auto_task.cancel()
    try:
        await auto_task
    except BaseException:
        pass

    pending = [e for e in events if e.event == "approval_pending"]
    completed = [e for e in events if e.event == "agent_completed"]
    # drize 应该直接复用 output（已 COMMITTED，不会过 HITL）
    # arch-designer 应该重跑（STARTED），过 HITL

    print(f"  approval_pending: {len(pending)} (应该 == 1，arch-designer)")
    print(f"  agent_completed: {len(completed)} (应该 == 2)")
    for c in completed:
        print(f"    ↳ {c.agent_name}")

    ok = len(pending) == 1 and len(completed) == 2
    print(f"  {PASS if ok else FAIL}: step0 复用 + step1 HITL 重跑")
    return ok


async def test_parallel_resume_hitl():
    """T3: PARALLEL 双 STARTED → resume → 双 HITL + gather"""
    print(f"\n{'─'*60}")
    print("T3: PARALLEL resume + 双 STARTED")
    print(f"{'─'*60}")

    clear_all_sessions()
    session_id = f"t3-par-{int(time.time())}"
    seed_parallel_both_started(session_id)

    start = asyncio.Event()
    auto_task = asyncio.create_task(auto_approve_all(start))
    start.set()

    engine = SchedulerEngine()
    events, sid = await engine.resume_from(session_id)
    await asyncio.sleep(0.5)
    auto_task.cancel()
    try:
        await auto_task
    except BaseException:
        pass

    pending = [e for e in events if e.event == "approval_pending"]
    completed = [e for e in events if e.event == "agent_completed"]

    print(f"  approval_pending: {len(pending)} (应该 == 2)")
    print(f"  agent_completed: {len(completed)} (应该 == 2)")

    ok = len(pending) == 2 and len(completed) == 2
    print(f"  {PASS if ok else FAIL}: 双 HITL + gather 全过")
    return ok


async def test_day8_regression():
    """T4: 全 COMMITTED → resume 直接跳过，不重跑（无 HITL）"""
    print(f"\n{'─'*60}")
    print("T4: Day 8 回归 — 全 COMMITTED resume 跳过")
    print(f"{'─'*60}")

    clear_all_sessions()
    session_id = f"t4-reg-{int(time.time())}"

    step_id = f"{session_id}::step0"
    with db_engine.begin() as conn:
        conn.execute(sa_text("""
            INSERT INTO sessions (session_id, mode, status, input_json, created_at)
            VALUES (:sid, 'DIRECT', 'completed', '{"prompt":"reg test"}', :ts)
        """), {"sid": session_id, "ts": _now_iso()})
        conn.execute(sa_text("""
            INSERT INTO agent_runs
            (session_id, agent_name, step_index, wave, input_json, llm_provider,
             llm_model, status, output_json, completed_at, idempotency_key, retry_count)
            VALUES (:sid, 'arch-designer', 0, 0, '{}', 'openrouter', 'mock',
                    'completed', '{"output":"cached output"}', :ts, :idem, 0)
        """), {"sid": session_id, "idem": step_id, "ts": _now_iso()})
        conn.execute(sa_text("""
            INSERT INTO checkpoints
            (session_id, step_id, step_index, status, input_hash, output_hash,
             committed_at, started_at, idempotency_key)
            VALUES (:sid, :step_id, 0, 'COMMITTED', :ih, :oh, :ts, :ts, :idem)
        """), {"sid": session_id, "step_id": step_id,
               "ih": hash("reg test"), "oh": hash("cached output"),
               "ts": _now_iso(), "idem": step_id})

    start = time.time()
    engine = SchedulerEngine()
    events, sid = await engine.resume_from(session_id)
    elapsed = time.time() - start

    pending = [e for e in events if e.event == "approval_pending"]
    completed = [e for e in events if e.event == "agent_completed"]

    print(f"  approval_pending: {len(pending)} (应该 == 0，已 COMMITTED 不重跑)")
    print(f"  agent_completed: {len(completed)} (应该 == 1)")
    print(f"  elapsed: {elapsed:.2f}s (应该 < 0.5s)")

    ok = len(pending) == 0 and len(completed) == 1 and elapsed < 1.0
    print(f"  {PASS if ok else FAIL}: 全 COMMITTED 跳过 = 无 HITL")
    return ok


async def main():
    print(f"\n{'═'*60}")
    print("Day 12: resume_from 补 HITL 验证（纯 Python）")
    print(f"{'═'*60}")

    results = []
    results.append(("T1 DIRECT resume HITL", await test_direct_resume_hitl()))
    results.append(("T2 SEQUENTIAL 混合 HITL", await test_sequential_resume_hitl()))
    results.append(("T3 PARALLEL 双 HITL", await test_parallel_resume_hitl()))
    results.append(("T4 Day 8 回归（全 COMMITTED 跳过）", await test_day8_regression()))

    print(f"\n{'═'*60}")
    passed = sum(1 for _, ok in results if ok)
    for name, ok in results:
        print(f"  {(PASS if ok else FAIL)} {name}")
    print(f"\n  📊 Day 12 验收: {passed}/{len(results)}")
    print(f"{'═'*60}\n")

    if passed == len(results):
        print("🎉 Day 12 resume_from 补 HITL 全绿 ✅")
        return 0
    return 1


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
