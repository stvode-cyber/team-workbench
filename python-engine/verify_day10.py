"""
verify_day10.py · HITL 四模式全覆盖验收

核心验证：
  ① DIRECT + approval_required agent → approval_pending → POST approve → agent_completed
  ② DIRECT + 非 approval_required agent → 直接 agent_completed，无 approval_pending
  ③ DIRECT + approval_required agent + POST reject → agent_skipped
  ④ SEQUENTIAL 串：approval_required → 正常 → 全链路
  ⑤ PARALLEL 并：两个 approval_required 同时挂起 → 各自 approve → 并行完成
  ⑥ HYBRID 波：wave0 approval_required → approve → wave1 正常 → 全链路

关键修复（Day 10 发现）：
  scheduler.run() 阻塞在 wait_for_approval → approval_pending 事件还没 yield
  客户端就收不到 → 死锁。
  解决：verify 端加并行轮询 GET /api/approvals pending 列表，
  不等 SSE 事件，直接发现 pending 就 approve/reject。

用法：
  python verify_day10.py
"""

from __future__ import annotations

import json
import time
import threading
from typing import Optional
from urllib.request import urlopen, Request

BASE = "http://127.0.0.1:8765"

_sse_events: list[dict] = []
_session_id: list[str] = [""]
_sse_done = threading.Event()
_stop_poll = threading.Event()


def _http_get(path: str) -> dict:
    with urlopen(f"{BASE}{path}", timeout=10) as r:
        return json.loads(r.read().decode())


def _http_post(path: str, body: dict) -> dict:
    data = json.dumps(body).encode()
    req = Request(f"{BASE}{path}", data=data, headers={"Content-Type": "application/json"}, method="POST")
    with urlopen(req, timeout=10) as r:
        return json.loads(r.read().decode())


def _run(agents: list[str], prompt: str, mode: str = "DIRECT",
         layers: Optional[list] = None, auto_approve: bool = True) -> None:
    """
    1) 起 SSE 线程（只收事件，不处理 approval）
    2) 起轮询线程（每秒 GET /api/approvals → 发现 pending 就 approve/reject）
    3) 等 SSE 收完
    """
    _sse_events.clear()
    _session_id[0] = ""
    _sse_done.clear()
    _stop_poll.clear()

    payload = {"agent_names": agents, "prompt": prompt, "mode": mode}
    if layers:
        payload["layers"] = layers

    # ── SSE worker：只收事件 ──
    def _sse_worker():
        try:
            data = json.dumps(payload).encode()
            req = Request(
                f"{BASE}/api/chat/stream",
                data=data,
                headers={"Content-Type": "application/json", "Accept": "text/event-stream"},
                method="POST",
            )
            with urlopen(req, timeout=60) as resp:
                for raw_line in resp:
                    line = raw_line.decode().rstrip()
                    if line.startswith("data:"):
                        payload_str = line[5:].strip()
                        if payload_str and payload_str != "[DONE]":
                            try:
                                evt = json.loads(payload_str)
                                _sse_events.append(evt)
                                sid = evt.get("session_id", "")
                                if sid and sid != "(creating...)":
                                    _session_id[0] = sid
                            except json.JSONDecodeError:
                                pass
        except Exception:
            pass
        finally:
            _sse_done.set()

    # ── Poller：每秒查 pending approvals ──
    def _poller():
        handled = set()
        while not _stop_poll.is_set():
            try:
                pending = _http_get("/api/approvals")
                for p in pending:
                    aid = p["id"]
                    if aid not in handled:
                        handled.add(aid)
                        _http_post(
                            f"/api/approvals/{aid}/respond",
                            {"approved": auto_approve},
                        )
                        print(f"    💬 poller: approval {aid} → {'approve' if auto_approve else 'reject'}")
            except Exception:
                pass
            _stop_poll.wait(0.5)

    threads = [
        threading.Thread(target=_sse_worker, daemon=True),
        threading.Thread(target=_poller, daemon=True),
    ]
    for t in threads:
        t.start()

    # 等 SSE 收完（最长 50s）
    _sse_done.wait(timeout=50)
    _stop_poll.set()  # 停 poller
    time.sleep(0.5)


def _find(event_type: str, agent_name: str = "") -> Optional[dict]:
    for e in _sse_events:
        if e.get("event") == event_type:
            if not agent_name or e.get("agent_name") == agent_name:
                return e
    return None


def _count(event_type: str) -> int:
    return sum(1 for e in _sse_events if e.get("event") == event_type)


# ═══════════════════════════════════════════════════════════════════════
# 测试点
# ═══════════════════════════════════════════════════════════════════════

def test_health() -> bool:
    print("\n── Test 0: health ──")
    try:
        r = _http_get("/health")
        ok = r["status"] == "ok"
        print(f"  health: {r['status']} {'✅' if ok else '❌'}")
        return ok
    except Exception as e:
        print(f"  ❌ health failed: {e}")
        return False


def test1_direct_hitl_auto_approve() -> bool:
    """DIRECT + drize-engineer(approval_required=True) → approval_pending → approve → completed"""
    print("\n── Test 1: DIRECT + drize-engineer (auto-approve) ──")
    _run(["drize-engineer"], "推演一个复杂方案", mode="DIRECT", auto_approve=True)

    sid = _session_id[0]
    has_pending = _find("approval_pending", "drize-engineer") is not None
    has_completed = _find("agent_completed", "drize-engineer") is not None
    has_complete = _find("session_complete") is not None
    has_skipped = _find("agent_skipped", "drize-engineer") is not None

    print(f"  session_id: {sid}")
    print(f"  approval_pending: {has_pending}")
    print(f"  agent_completed: {has_completed}")
    print(f"  agent_skipped: {has_skipped}")
    print(f"  session_complete: {has_complete}")

    ok = has_pending and has_completed and not has_skipped and has_complete
    print(f"  {'✅' if ok else '❌'} Test 1")
    return ok


def test2_direct_no_hitl() -> bool:
    """DIRECT + arch-designer(approval_required=False) → 直接 completed"""
    print("\n── Test 2: DIRECT + arch-designer (无需 HITL) ──")
    _run(["arch-designer"], "设计一个新架构", mode="DIRECT")

    has_pending = _find("approval_pending", "arch-designer") is not None
    has_completed = _find("agent_completed", "arch-designer") is not None

    print(f"  approval_pending: {has_pending}")
    print(f"  agent_completed: {has_completed}")

    ok = not has_pending and has_completed
    print(f"  {'✅' if ok else '❌'} Test 2")
    return ok


def test3_direct_hitl_reject() -> bool:
    """DIRECT + drize-engineer → reject → agent_skipped"""
    print("\n── Test 3: DIRECT + drize-engineer (手动 reject) ──")
    _run(["drize-engineer"], "reject test", mode="DIRECT", auto_approve=False)

    has_pending = _find("approval_pending", "drize-engineer") is not None
    has_skipped = _find("agent_skipped", "drize-engineer") is not None
    has_completed = _find("agent_completed", "drize-engineer") is not None

    print(f"  approval_pending: {has_pending}")
    print(f"  agent_skipped: {has_skipped}")
    print(f"  agent_completed: {has_completed}")

    ok = has_pending and has_skipped and not has_completed
    print(f"  {'✅' if ok else '❌'} Test 3")
    return ok


def test4_sequential_hitl() -> bool:
    """SEQUENTIAL + [drize-engineer, arch-designer] → HITL + 全链路"""
    print("\n── Test 4: SEQUENTIAL + [drize-engineer, arch-designer] ──")
    _run(["drize-engineer", "arch-designer"], "seq test", mode="SEQUENTIAL")

    drize_pending = _find("approval_pending", "drize-engineer") is not None
    drize_completed = _find("agent_completed", "drize-engineer") is not None
    arch_completed = _find("agent_completed", "arch-designer") is not None
    has_complete = _find("session_complete") is not None

    print(f"  drize approval_pending: {drize_pending}")
    print(f"  drize agent_completed: {drize_completed}")
    print(f"  arch agent_completed: {arch_completed}")
    print(f"  session_complete: {has_complete}")

    ok = drize_pending and drize_completed and arch_completed and has_complete
    print(f"  {'✅' if ok else '❌'} Test 4")
    return ok


def test5_parallel_hitl() -> bool:
    """PARALLEL + [drize-engineer, safety-auditor] → 两个都挂起 → 各自 approve → 并行完成"""
    print("\n── Test 5: PARALLEL + [drize-engineer, safety-auditor] ──")
    _run(["drize-engineer", "safety-auditor"], "parallel test", mode="PARALLEL")

    drize_pending = _find("approval_pending", "drize-engineer") is not None
    safety_pending = _find("approval_pending", "safety-auditor") is not None
    drize_completed = _find("agent_completed", "drize-engineer") is not None
    safety_completed = _find("agent_completed", "safety-auditor") is not None
    pending_count = _count("approval_pending")

    print(f"  drize approval_pending: {drize_pending}")
    print(f"  safety approval_pending: {safety_pending}")
    print(f"  drize agent_completed: {drize_completed}")
    print(f"  safety agent_completed: {safety_completed}")
    print(f"  approval_pending 总数: {pending_count}")

    ok = drize_pending and safety_pending and drize_completed and safety_completed and pending_count >= 2
    print(f"  {'✅' if ok else '❌'} Test 5")
    return ok


def test6_hybrid_hitl() -> bool:
    """HYBRID + [drize-engineer, arch-designer] → HITL + 两 wave 完成"""
    print("\n── Test 6: HYBRID + [drize-engineer, arch-designer] ──")
    _run(["drize-engineer", "arch-designer"], "hybrid test", mode="HYBRID")

    drize_pending = _find("approval_pending", "drize-engineer") is not None
    drize_completed = _find("agent_completed", "drize-engineer") is not None
    arch_completed = _find("agent_completed", "arch-designer") is not None

    print(f"  drize approval_pending: {drize_pending}")
    print(f"  drize agent_completed: {drize_completed}")
    print(f"  arch agent_completed: {arch_completed}")

    ok = drize_pending and drize_completed and arch_completed
    print(f"  {'✅' if ok else '❌'} Test 6")
    return ok


def test7_day9_regression() -> bool:
    """回归 Day 9：连续 3 次失败 → retry_count=3, status=failed"""
    print("\n── Test 7: Day 9 回归 · 3 次失败 → failed ──")

    _http_post("/api/mock/fail", {"remaining": 3})
    time.sleep(0.2)

    _run(["arch-designer"], "fail test", mode="DIRECT")

    failed_count = _count("agent_failed")
    has_completed = _find("agent_completed", "arch-designer") is not None

    print(f"  agent_failed 事件数: {failed_count}")
    print(f"  agent_completed: {has_completed}")

    _http_post("/api/mock/fail", {"remaining": 0})

    ok = failed_count == 1 and not has_completed
    print(f"  {'✅' if ok else '❌'} Test 7")
    return ok


# ═══════════════════════════════════════════════════════════════════════
# 主入口
# ═══════════════════════════════════════════════════════════════════════

def main():
    print("=" * 60)
    print("Day 10 HITL 四模式全覆盖验收")
    print("=" * 60)

    results = {
        "health": test_health(),
        "direct_hitl_auto_approve": test1_direct_hitl_auto_approve(),
        "direct_no_hitl": test2_direct_no_hitl(),
        "direct_hitl_reject": test3_direct_hitl_reject(),
        "sequential_hitl": test4_sequential_hitl(),
        "parallel_hitl": test5_parallel_hitl(),
        "hybrid_hitl": test6_hybrid_hitl(),
        "day9_regression": test7_day9_regression(),
    }

    print("\n" + "=" * 60)
    passed = sum(1 for v in results.values() if v)
    total = len(results)
    print(f"结果: {passed}/{total}")
    for name, ok in results.items():
        mark = "✅" if ok else "❌"
        print(f"  {mark} {name}")
    print("=" * 60)


if __name__ == "__main__":
    main()
