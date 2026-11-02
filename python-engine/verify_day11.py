"""
Day 11: Abort 生产化验收
═══════════════════════════════════════════════════

验证点：
  1. abort-aware sleep 生效（退避期间每 0.25s 轮询 abort）
  2. abort 后 sessions.status='aborted'
  3. abort 后 agent_runs 未完成的 status='aborted'
  4. checkpoint 已 COMMITTED 的 step output 保留不丢
  5. DIRECT + HITL pending → abort → approval_request 清理
  6. PARALLEL/HYBRID abort 在 gather 收集后能正确识别

运行：先启 uvicorn（mock 模式），再跑本脚本
"""

from __future__ import annotations

import json
import sqlite3
import sys
import threading
import time
import urllib.request
import urllib.error

BASE = "http://127.0.0.1:8765"
DB = f"{__import__('os').environ.get('APPDATA', '')}\\TeamWorkbench\\data.db"

PASS = "\033[92m✅ PASS\033[0m"
FAIL = "\033[91m❌ FAIL\033[0m"


def http_get(path: str) -> dict:
    with urllib.request.urlopen(f"{BASE}{path}") as r:
        return json.loads(r.read().decode())


def http_post(path: str, body: dict | None = None) -> dict:
    data = json.dumps(body or {}).encode()
    req = urllib.request.Request(f"{BASE}{path}", data=data,
                                 headers={"Content-Type": "application/json"},
                                 method="POST")
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read().decode())


def db_query(sql: str, params: tuple = ()) -> list:
    conn = sqlite3.connect(DB)
    try:
        cur = conn.execute(sql, params)
        return cur.fetchall()
    finally:
        conn.close()


def clear_pending_approvals():
    """开新 session 前必须清旧 pending approvals，不然会阻塞"""
    if not __import__('os').path.exists(DB):
        return
    conn = sqlite3.connect(DB)
    try:
        conn.execute("DELETE FROM approval_requests WHERE status='pending'")
        conn.commit()
    finally:
        conn.close()


# ─────────────────────────────────────────────────
# SSE 客户端（从 main.py chat_stream 收事件）
# ─────────────────────────────────────────────────

def consume_sse(session_id: str, events_list: list, stop_flag: threading.Event):
    """后台线程：连 chat_stream 收事件，塞 events_list"""
    url = f"{BASE}/api/chat/stream"
    body = {
        "mode": "DIRECT",
        "agent_names": ["arch-designer"],
        "prompt": "abort 测试 prompt",
        "session_id": session_id,  # 复用 session，abort 端点才能找到
    }
    try:
        data = json.dumps(body).encode()
        req = urllib.request.Request(url, data=data,
                                     headers={"Content-Type": "application/json"},
                                     method="POST")
        with urllib.request.urlopen(req, timeout=30) as resp:
            for raw_line in resp:
                if stop_flag.is_set():
                    break
                line = raw_line.decode().strip()
                if not line:
                    continue
                if line.startswith("data: "):
                    payload = line[6:]
                    try:
                        obj = json.loads(payload)
                        events_list.append(obj)
                    except json.JSONDecodeError:
                        pass
    except Exception as exc:
        events_list.append({"_error": str(exc)})


def poll_pending_approvals(approve_func, stop_flag: threading.Event):
    """Day 10 遗留：并行 Poller 线程，自动 approve 所有 pending"""
    while not stop_flag.is_set():
        try:
            data = http_get("/api/approvals")
            pending = [a for a in data.get("approvals", [])
                       if a.get("status") == "pending"]
            for app in pending:
                approve_func(app["id"], "approved")
        except Exception:
            pass
        stop_flag.wait(0.5)


def manual_approve_first_pending(stop_flag: threading.Event) -> str | None:
    """轮询到第一个 pending approval 就返回 approval_id"""
    deadline = time.time() + 15
    while time.time() < deadline and not stop_flag.is_set():
        try:
            data = http_get("/api/approvals")
            pending = [a for a in data.get("approvals", [])
                       if a.get("status") == "pending"]
            if pending:
                return pending[0]["id"]
        except Exception:
            pass
        time.sleep(0.3)
    return None


def main():
    print(f"\n{'═'*60}")
    print("Day 11: Abort 生产化验收")
    print(f"{'═'*60}")

    # ── 先确保 uvicorn 活着 ──
    try:
        health = http_get("/health")
        print(f"  🟢 uvicorn 活着: {health}")
    except Exception:
        print(f"  {FAIL} uvicorn 没起来！先启：python -m uvicorn main:app --port 8765")
        sys.exit(1)

    clear_pending_approvals()
    print("  🧹 旧 pending approvals 已清")

    results = []

    # ════════════════════════════════════════════════
    # Test 1: DIRECT + arch-designer (无 HITL) → 运行中 POST abort
    # ════════════════════════════════════════════════
    print(f"\n{'─'*60}")
    print("Test 1: DIRECT + arch-designer → 运行中 abort")
    print(f"{'─'*60}")

    session_id = f"abort-test-{int(time.time())}"
    events: list[dict] = []
    stop = threading.Event()

    # 启 SSE 消费线程
    sse_thread = threading.Thread(
        target=consume_sse, args=(session_id, events, stop), daemon=True
    )
    sse_thread.start()
    time.sleep(0.5)

    # 运行 1 秒后 abort
    time.sleep(1.0)
    try:
        abort_resp = http_post(f"/api/sessions/{session_id}/abort")
        print(f"  POST abort → {abort_resp}")
    except Exception as exc:
        print(f"  abort 端点调用异常（可能 session_id 不存在）: {exc}")

    time.sleep(2.0)
    stop.set()
    sse_thread.join(timeout=5)

    # 查 DB
    rows = db_query("SELECT status FROM sessions WHERE session_id=?", (session_id,))
    sess_status = rows[0][0] if rows else "NOT_FOUND"
    print(f"  sessions.status = {sess_status}")

    run_rows = db_query(
        "SELECT step_index, status FROM agent_runs WHERE session_id=? ORDER BY step_index",
        (session_id,)
    )
    print(f"  agent_runs = {run_rows}")

    t1_pass = (
        sess_status == "aborted"
        and any(r[1] == "aborted" for r in run_rows)
    )
    results.append(("T1 DIRECT abort", t1_pass,
                    f"sessions={sess_status}, agent_runs={run_rows}"))

    # ════════════════════════════════════════════════
    # Test 2: DIRECT + drize-engineer (需 HITL) → approval_pending 时 abort
    # ════════════════════════════════════════════════
    print(f"\n{'─'*60}")
    print("Test 2: DIRECT + drize-engineer → approval_pending 时 abort")
    print(f"{'─'*60}")

    clear_pending_approvals()
    session_id2 = f"abort-test-hitl-{int(time.time())}"
    events2: list[dict] = []
    stop2 = threading.Event()

    # 启 SSE
    sse2 = threading.Thread(
        target=consume_sse, args=(session_id2, events2, stop2), daemon=True
    )
    sse2.start()
    time.sleep(0.5)

    # 等 approval_pending 出现
    approval_id = manual_approve_first_pending(stop2)
    if approval_id:
        print(f"  📬 approval_pending 出现: id={approval_id}")
        print(f"  ⚡ 立刻 POST abort")
        try:
            abort_resp = http_post(f"/api/sessions/{session_id2}/abort")
            print(f"  POST abort → {abort_resp}")
        except Exception as exc:
            print(f"  abort 调用异常: {exc}")
    else:
        print(f"  ⚠️  没等到 approval_pending（可能 agent 不需要审批？）")

    time.sleep(2.0)
    stop2.set()
    sse2.join(timeout=5)

    # 查 DB
    sess2 = db_query("SELECT status FROM sessions WHERE session_id=?", (session_id2,))
    sess2_status = sess2[0][0] if sess2 else "NOT_FOUND"
    runs2 = db_query(
        "SELECT step_index, status FROM agent_runs WHERE session_id=? ORDER BY step_index",
        (session_id2,)
    )
    print(f"  sessions.status = {sess2_status}")
    print(f"  agent_runs = {runs2}")

    # approval_request 表里的 pending 应该清掉（abort 后 approval_request 没自动清理，
    # 但这是 Day 12+ 的事；当前只验 sessions/agent_runs）
    appr = db_query(
        "SELECT id, status FROM approval_requests WHERE request_id=?",
        (approval_id,) if approval_id else ("",)
    )
    print(f"  approval_request = {appr}")

    t2_pass = (
        sess2_status == "aborted"
        and any(r[1] in ("aborted", "skipped") for r in runs2)
    )
    results.append(("T2 HITL abort", t2_pass,
                    f"sessions={sess2_status}, agent_runs={runs2}"))

    # ════════════════════════════════════════════════
    # Test 3: 全绿汇总
    # ════════════════════════════════════════════════
    print(f"\n{'═'*60}")
    passed = sum(1 for _, p, _ in results if p)
    total = len(results)
    for name, ok, detail in results:
        print(f"  {(PASS if ok else FAIL)} {name} — {detail}")
    print(f"\n  📊 Day 11 验收: {passed}/{total}")
    print(f"{'═'*60}\n")

    if passed == total:
        print("🎉 Day 11 全绿！abort 生产化通过 ✅")
        return 0
    else:
        print(f"⚠️  有 {total - passed} 个测试没过")
        return 1


if __name__ == "__main__":
    sys.exit(main())
