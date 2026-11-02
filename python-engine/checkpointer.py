"""
Checkpointer · SQLite WAL durable execution

精确对齐指南针 D6 + L194-197（MeshFlow 生产模式）：
  INSERT STARTED → 执行 → INSERT COMMITTED
  恢复时：STARTED 无 COMMITTED → 重跑；COMMITTED → 跳过

output_data 用 gzip 压缩（指南针 SQL 是 BLOB）。
"""

from __future__ import annotations

import gzip
import hashlib
import json
from datetime import datetime
from typing import Optional

from sqlalchemy import text

from db import engine


# ============================================================
# 辅助
# ============================================================

def _now_iso() -> str:
    return datetime.utcnow().isoformat() + "Z"


def _sha256(data: str | bytes) -> str:
    if isinstance(data, str):
        data = data.encode("utf-8")
    return hashlib.sha256(data).hexdigest()


def _gzip_compress(s: str) -> bytes:
    return gzip.compress(s.encode("utf-8"))


def _gzip_decompress(b: bytes) -> str:
    return gzip.decompress(b).decode("utf-8")


# ============================================================
# Checkpointer
# ============================================================

class Checkpointer:
    """三步 checkpoint：STARTED → RUNNING → COMMITTED"""

    def save_step_started(
        self,
        session_id: str,
        step_id: str,
        step_index: int,
        input_data: str,
        idempotency_key: str,
    ) -> None:
        """步骤开始：写 STARTED（如果已 COMMITTED 则跳过）"""
        input_hash = _sha256(input_data)
        now = _now_iso()

        with engine.begin() as conn:
            # 幂等：已 COMMITTED 不覆盖
            existing = conn.execute(text("""
                SELECT status FROM checkpoints
                WHERE session_id = :sid AND step_id = :sid2
            """), {"sid": session_id, "sid2": step_id}).fetchone()

            if existing and existing[0] == "COMMITTED":
                print(f"  [checkpoint] step {step_id} 已 COMMITTED → 跳过 STARTED")
                return

            conn.execute(text("""
                INSERT OR REPLACE INTO checkpoints
                (session_id, step_id, step_index, status, input_hash,
                 started_at, idempotency_key)
                VALUES (:sid, :stepid, :si, 'STARTED', :ih, :ts, :idem)
            """), {
                "sid": session_id,
                "stepid": step_id,
                "si": step_index,
                "ih": input_hash,
                "ts": now,
                "idem": idempotency_key,
            })

    def save_step_committed(
        self,
        session_id: str,
        step_id: str,
        output: str,
    ) -> None:
        """步骤完成：写 COMMITTED + output_data（gzip）

        幂等：不管 checkpoint 之前有没有行，都能写入（INSERT OR REPLACE）。
        解决 checkpoint 被手动清空后 resume_from 重跑 → save_step_committed
        是 UPDATE → 没行可改 → output 丢失的问题。
        """
        output_hash = _sha256(output)
        compressed = _gzip_compress(output)
        now = _now_iso()

        with engine.begin() as conn:
            # 先查 step_index（INSERT OR REPLACE 需要这些列）
            row = conn.execute(text("""
                SELECT step_index FROM checkpoints
                WHERE session_id = :sid AND step_id = :stepid
            """), {"sid": session_id, "stepid": step_id}).fetchone()
            step_index = row[0] if row else int(step_id.rsplit("step", 1)[-1])

            conn.execute(text("""
                INSERT OR REPLACE INTO checkpoints
                (session_id, step_id, step_index, status, output_hash,
                 output_data, committed_at, idempotency_key)
                VALUES (:sid, :stepid, :si, 'COMMITTED', :oh, :od, :ts, :idem)
            """), {
                "sid": session_id,
                "stepid": step_id,
                "si": step_index,
                "oh": output_hash,
                "od": compressed,
                "ts": now,
                "idem": step_id,
            })

    def is_committed(self, session_id: str, step_id: str) -> bool:
        """恢复时查：这个 step 有没有 COMMITTED？"""
        with engine.connect() as conn:
            row = conn.execute(text("""
                SELECT status FROM checkpoints
                WHERE session_id = :sid AND step_id = :stepid
            """), {"sid": session_id, "stepid": step_id}).fetchone()
            return row is not None and row[0] == "COMMITTED"

    def get_committed_output(self, session_id: str, step_id: str) -> Optional[str]:
        """恢复时取：已 COMMITTED 的 output_data（解压）

        边界兜底：
          ① row 不存在 → None
          ② output_data 是 NULL → None
          ③ output_data 是空 bytes → None（gzip 解压会崩，提前拦）
          ④ output_data 不是 gzip 格式（被篡改）→ try/except 返 None
        """
        import gzip as _gzip
        with engine.connect() as conn:
            row = conn.execute(text("""
                SELECT output_data FROM checkpoints
                WHERE session_id = :sid AND step_id = :stepid AND status = 'COMMITTED'
            """), {"sid": session_id, "stepid": step_id}).fetchone()
            if row is None or row[0] is None:
                return None
            if len(row[0]) == 0:
                return None
            try:
                return _gzip_decompress(row[0])
            except Exception:
                return None

    # ── 恢复：找出 session 里所有 STARTED 无 COMMITTED 的 step ──

    def find_uncommitted(self, session_id: str) -> list[dict]:
        """
        返回 [{step_id, step_index, status}]
        规则：status='STARTED' 且 committed_at IS NULL
        （即执行到一半挂了，需要重跑）
        """
        with engine.connect() as conn:
            rows = conn.execute(text("""
                SELECT step_id, step_index, status
                FROM checkpoints
                WHERE session_id = :sid AND status = 'STARTED'
                ORDER BY step_index
            """), {"sid": session_id}).fetchall()
            return [
                {"step_id": r[0], "step_index": r[1], "status": r[2]}
                for r in rows
            ]

    def get_all_steps(self, session_id: str) -> list[dict]:
        """恢复用：session 所有 checkpoint（已 COMMITTED + STARTED 未完成）"""
        with engine.connect() as conn:
            rows = conn.execute(text("""
                SELECT step_id, step_index, status, committed_at
                FROM checkpoints
                WHERE session_id = :sid
                ORDER BY step_index
            """), {"sid": session_id}).fetchall()
            return [
                {
                    "step_id": r[0],
                    "step_index": r[1],
                    "status": r[2],
                    "committed_at": r[3],
                }
                for r in rows
            ]
