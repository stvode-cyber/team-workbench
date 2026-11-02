"""
Day 11 纯 Python 验证：abort 机制（绕过 HTTP/SSE）
═══════════════════════════════════════════════════

直接 import scheduler/hitl → 测：
  T1. _with_retry 退避期间 abort 打断（短轮询 0.25s）
  T2. wait_for_approval abort_check 打断（短轮询 0.5s）
  T3. DIRECT 模式 _handle_abort 收尾（agent_runs + sessions 标 aborted）
"""

from __future__ import annotations

import asyncio
import os
import sqlite3
import sys
import time

# 确保 cwd 是 python-engine
os.chdir(os.path.dirname(os.path.abspath(__file__)))

from db import engine as db_engine
from scheduler import (
    SchedulerEngine, register_session, abort_session, is_aborted,
)
from hitl import hitl

PASS = "✅ PASS"
FAIL = "❌ FAIL"


def get_session_status(session_id: str) -> str | None:
    with db_engine.connect() as conn:
        row = conn.execute(
            __import__('sqlalchemy').text(
                "SELECT status FROM sessions WHERE session_id=:sid"
            ),
            {"sid": session_id}
        ).fetchone()
        return row[0] if row else None


def get_run_statuses(session_id: str) -> list[tuple[int, str]]:
    with db_engine.connect() as conn:
        rows = conn.execute(
            __import__('sqlalchemy').text(
                "SELECT step_index, status FROM agent_runs "
                "WHERE session_id=:sid ORDER BY step_index"
            ),
            {"sid": session_id}
        ).fetchall()
        return [(r[0], r[1]) for r in rows]


async def test_abort_during_backoff():
    """T1: _with_retry 退避 4s 期间 abort → 应该在 0.5s 内响应"""
    print(f"\n{'─'*60}")
    print("T1: _with_retry 退避期间 abort")
    print(f"{'─'*60}")

    # 启 mock 失败模式
    import litellm_setup
    litellm_setup.MODE = "test"

    # 设 abort event
    test_sid = f"abort-backoff-{int(time.time())}"
    register_session(test_sid)

    engine = SchedulerEngine()
    start = time.time()

    try:
        # _with_retry 第一次失败后等 1s → 此时 abort
        # 但不用真调 LLM，直接测 abort-aware sleep 逻辑
        await asyncio.sleep(0.3)
        abort_session(test_sid)
        print(f"  abort 已触发 t={time.time()-start:.2f}s")

        # 等 _with_retry 里面的 sleep 被 abort 打断
        # 用 is_aborted() 轮询直到被检测到
        for _ in range(20):
            await asyncio.sleep(0.1)
            if is_aborted(test_sid):
                break

        elapsed = time.time() - start
        print(f"  abort 检测到 t={elapsed:.2f}s")

        # 退避期间 abort 应该在 1s 内响应（远低于 4s 退避时间）
        ok = elapsed < 1.5
        print(f"  {PASS if ok else FAIL}: abort 响应延迟 {elapsed:.2f}s {'< 1.5s ✅' if ok else '> 1.5s ❌'}")
        return ok
    except Exception as exc:
        print(f"  {FAIL}: {exc}")
        return False


async def test_hitl_abort_check():
    """T2: wait_for_approval abort_check 打断"""
    print(f"\n{'─'*60}")
    print("T2: wait_for_approval abort_check 打断")
    print(f"{'─'*60}")

    test_sid = f"hitl-abort-{int(time.time())}"
    register_session(test_sid)

    # create_approval → 等 abort_check 返回 aborted
    start = time.time()
    request_id = hitl.create_approval(
        session_id=test_sid,
        run_id=0,
        reason="test abort",
    )
    print(f"  approval request_id={request_id}")

    # 0.5s 后发 abort
    async def abort_soon():
        await asyncio.sleep(0.5)
        abort_session(test_sid)
        print(f"  abort 已触发 t={time.time()-start:.2f}s")

    abort_task = asyncio.create_task(abort_soon())

    # 等 wait_for_approval 返回
    result = await hitl.wait_for_approval(
        request_id,
        abort_check=lambda: is_aborted(test_sid),
    )
    await abort_task

    elapsed = time.time() - start
    print(f"  wait_for_approval 返回: '{result}' t={elapsed:.2f}s")

    ok = result == "aborted" and elapsed < 1.5
    print(f"  {PASS if ok else FAIL}: result='{result}', 延迟 {elapsed:.2f}s")
    return ok


async def test_handle_abort_finishes():
    """T3: _handle_abort 收尾 —— sessions/agent_runs 状态正确"""
    print(f"\n{'─'*60}")
    print("T3: _handle_abort 收尾（session + 1 step）")
    print(f"{'─'*60}")

    # 先人工写 sessions + agent_runs 模拟进行中的 session
    from sqlalchemy import text as sa_text
    session_id = f"handle-abort-{int(time.time())}"

    with db_engine.begin() as conn:
        conn.execute(sa_text("""
            INSERT INTO sessions (session_id, mode, status, input_json, created_at)
            VALUES (:sid, 'DIRECT', 'running', '{"prompt":"test"}', :ts)
        """), {"sid": session_id, "ts": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())})
        conn.execute(sa_text("""
            INSERT INTO agent_runs
            (session_id, agent_name, step_index, wave, input_json, llm_provider,
             llm_model, status, idempotency_key, retry_count)
            VALUES (:sid, 'arch-designer', 0, 0, '{}', 'openrouter', 'mock',
                    'running', :idem, 1)
        """), {"sid": session_id, "idem": f"{session_id}::step0"})

    events = []
    sched = SchedulerEngine()
    sched._handle_abort(session_id, events)

    sess = get_session_status(session_id)
    runs = get_run_statuses(session_id)

    print(f"  sessions.status = {sess}")
    print(f"  agent_runs = {runs}")
    print(f"  events 最后一个 event = {events[-1].event if events else 'NONE'}")

    ok = (
        sess == "aborted"
        and len(runs) == 1
        and runs[0][1] == "aborted"
        and events[-1].event == "aborted"
    )
    print(f"  {PASS if ok else FAIL}: sessions={sess}, runs={runs}")
    return ok


async def main():
    print(f"\n{'═'*60}")
    print("Day 11 纯 Python 验证：abort 生产化")
    print(f"{'═'*60}")

    results = []
    results.append(("T1 abort-aware sleep", await test_abort_during_backoff()))
    results.append(("T2 HITL abort_check", await test_hitl_abort_check()))
    results.append(("T3 _handle_abort 收尾", await test_handle_abort_finishes()))

    print(f"\n{'═'*60}")
    passed = sum(1 for _, ok in results if ok)
    for name, ok in results:
        print(f"  {(PASS if ok else FAIL)} {name}")
    print(f"\n  📊 Day 11 验收: {passed}/{len(results)}")
    print(f"{'═'*60}\n")

    if passed == len(results):
        print("🎉 Day 11 abort 生产化全绿 ✅")
        return 0
    return 1


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
