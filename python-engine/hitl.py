"""
HITL 审批门 · approval_requests + 30min 超时自动 reject

精确对齐指南针 D7 + L222-234 + L283 + L301-302。

核心机制：
  1) Scheduler agent 执行前 → 如果 approval_required → 写 approval_request(status=pending)
  2) SSE 推 approval_pending 事件（带 expires_at）
  3) 挂起等待：asyncio.wait_for(event.wait(), timeout=1800)  # 30min
  4) 超时 → status=timeout → agent 跳过
  5) POST /api/approvals/{id}/respond → set(event) → scheduler 继续

跨请求通信：全局 _approval_events dict（request_id → asyncio.Event）
"""

from __future__ import annotations

import asyncio
from datetime import datetime, timedelta
from typing import Optional

from sqlalchemy import text

from db import engine


# ============================================================
# 全局通信：request_id → asyncio.Event
# ============================================================

_approval_events: dict[int, asyncio.Event] = {}


def _now_iso() -> str:
    return datetime.utcnow().isoformat() + "Z"


def _plus_30min() -> str:
    return (datetime.utcnow() + timedelta(minutes=30)).isoformat() + "Z"


# ============================================================
# HITLManager
# ============================================================

class HITLManager:
    """HITL 审批门管理"""

    def __init__(self) -> None:
        self._events = _approval_events  # 全局共享

    def create_approval(
        self,
        session_id: str,
        run_id: int,
        reason: str,
        request_payload: str = "",
    ) -> int:
        """写 approval_requests(status=pending) → 返回 request_id"""
        with engine.begin() as conn:
            result = conn.execute(text("""
                INSERT INTO approval_requests
                (session_id, run_id, reason, request_payload, status, expires_at, created_at)
                VALUES (:sid, :rid, :reason, :payload, 'pending', :expires, :ts)
            """), {
                "sid": session_id,
                "rid": run_id,
                "reason": reason,
                "payload": request_payload,
                "expires": _plus_30min(),
                "ts": _now_iso(),
            })
            request_id = result.lastrowid

        # 注册 Event（跨请求通信）
        self._events[request_id] = asyncio.Event()
        print(f"  ⏸ HITL 审批门: request_id={request_id} | run_id={run_id} | reason={reason[:50]}...")
        return request_id

    async def wait_for_approval(
        self,
        request_id: int,
        timeout_seconds: int = 1800,  # 30min 默认
        abort_check: Optional[callable] = None,  # Day 11: 返回 bool 的 abort 检查
    ) -> str:
        """
        等待审批结果：
          approved  → 返回 "approved"
          rejected  → 返回 "rejected"
          timeout   → 返回 "timeout"
          aborted   → 返回 "aborted"（Day 11: abort 打断 HITL）

        改用短轮询替代 await event.wait()，因为 event.wait() 无法被外部 abort 打断。
        每 0.5s 检查一次 abort_check()，响应延迟 <0.5s。
        """
        event = self._events.get(request_id)
        if event is None:
            return "timeout"

        elapsed = 0.0
        poll_interval = 0.5
        while elapsed < timeout_seconds:
            if event.is_set():
                break
            if abort_check and abort_check():
                print(f"  🛑 abort 打断 HITL wait: request_id={request_id}")
                return "aborted"
            await asyncio.sleep(poll_interval)
            elapsed += poll_interval

        # 超时或事件触发后继续
        if not event.is_set():
            self._mark_timeout(request_id)
            print(f"  ⏰ HITL 超时自动 reject: request_id={request_id}")
            return "timeout"

        # 读最终状态
        with engine.connect() as conn:
            row = conn.execute(text("""
                SELECT status FROM approval_requests WHERE id = :id
            """), {"id": request_id}).fetchone()
        return row[0] if row else "timeout"

    def respond(self, request_id: int, approved: bool, approver: str = "C#-Client") -> None:
        """approve 端点调用：approved=True/False → 更新 DB → set Event"""
        new_status = "approved" if approved else "rejected"
        with engine.begin() as conn:
            conn.execute(text("""
                UPDATE approval_requests
                SET status = :s, approver = :a, response_at = :ts
                WHERE id = :id
            """), {"s": new_status, "a": approver, "ts": _now_iso(), "id": request_id})

        # 唤醒等待中的 scheduler
        event = self._events.get(request_id)
        if event:
            event.set()
        print(f"  ✅ HITL respond: request_id={request_id} → {new_status}")

    def _mark_timeout(self, request_id: int) -> None:
        """超时自动 reject"""
        with engine.begin() as conn:
            conn.execute(text("""
                UPDATE approval_requests
                SET status = 'timeout', response_at = :ts
                WHERE id = :id AND status = 'pending'
            """), {"ts": _now_iso(), "id": request_id})

    def get_pending(self) -> list[dict]:
        """C# 前端初始化时拉待审批列表"""
        with engine.connect() as conn:
            rows = conn.execute(text("""
                SELECT id, session_id, run_id, reason, expires_at
                FROM approval_requests
                WHERE status = 'pending'
                ORDER BY id
            """)).fetchall()
        return [
            {
                "id": r[0],
                "session_id": r[1],
                "run_id": r[2],
                "reason": r[3],
                "expires_at": r[4],
            }
            for r in rows
        ]


# 模块级单例（main.py 和 scheduler.py 都用这个）
hitl = HITLManager()
