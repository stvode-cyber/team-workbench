"""
SQLite schema 对齐（精确对齐 v3.1.0-融合指南针 ④）

设计：
  - 一个 SQLite 文件，C# WPF 和 Python Scheduler 共用
  - 启动时 PRAGMA: WAL + NORMAL + busy_timeout=5000（多进程并发安全）
  - 7 张新表 + 4 个索引，C# 已有的 3 张（agents/install_log/status_events）不动
  - SQLAlchemy 2.0 风格 + Base.metadata.create_all() 幂等建表

用法：
  from db import SessionLocal, engine
  # 直接 import 就自动建表
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import Optional

from sqlalchemy import (
    INTEGER,
    TEXT,
    REAL,
    BLOB,
    Column,
    ForeignKey,
    Index,
    UniqueConstraint,
    event,
    text,
    create_engine,
)
from sqlalchemy.orm import DeclarativeBase, Session, sessionmaker


# ============================================================
# 连接串 —— 和 C# WPF 共用同一个 SQLite 文件
# 环境变量 TEAMWORKBENCH_DB 可覆盖（测试用）
# ============================================================

def _default_db_path() -> str:
    """默认路径：%AppData%/TeamWorkbench/data.db（C# Initialize 也用这个）"""
    appdata = os.environ.get("APPDATA") or os.path.expanduser("~/.config")
    return str(Path(appdata) / "TeamWorkbench" / "data.db")


DB_PATH = os.environ.get("TEAMWORKBENCH_DB", _default_db_path())

# 确保父目录存在
Path(DB_PATH).parent.mkdir(parents=True, exist_ok=True)


# ============================================================
# Engine + 3 PRAGMA（连接事件里跑，每次连接都生效）
# ============================================================

engine = create_engine(
    f"sqlite:///{DB_PATH}",
    connect_args={"check_same_thread": False},  # SQLite 多线程
    echo=False,
)


@event.listens_for(engine, "connect")
def _set_sqlite_pragma(dbapi_connection, _connection_record):
    """启动时 3 PRAGMA（指南针 ④ L147-150）：
    - journal_mode=WAL        # 多进程并发读写安全
    - synchronous=NORMAL      # WAL 模式下性能/安全最佳平衡
    - busy_timeout=5000       # 另一进程锁 DB，等 5s
    """
    cursor = dbapi_connection.cursor()
    cursor.execute("PRAGMA journal_mode=WAL")
    cursor.execute("PRAGMA synchronous=NORMAL")
    cursor.execute("PRAGMA busy_timeout=5000")
    cursor.close()


SessionLocal = sessionmaker(bind=engine, autocommit=False, autoflush=False, expire_on_commit=False)


# ============================================================
# Base + 7 张新表模型（精确对齐指南针 ④）
# ============================================================

class Base(DeclarativeBase):
    pass


# ─── sessions：每次调度会话 ───
class SessionModel(Base):
    __tablename__ = "sessions"

    id = Column(INTEGER, primary_key=True, autoincrement=True)
    session_id = Column(TEXT, unique=True, nullable=False)      # UUID v4
    name = Column(TEXT)                                         # 用户给会话起的名
    mode = Column(TEXT, nullable=False)                         # DIRECT/SEQUENTIAL/PARALLEL/HYBRID
    status = Column(TEXT, nullable=False, default="pending")   # pending/running/paused/completed/failed
    input_json = Column(TEXT)                                   # 会话输入（prompt + 上下文）
    output_json = Column(TEXT)                                  # 最终输出
    error_message = Column(TEXT)
    started_at = Column(TEXT)
    completed_at = Column(TEXT)
    created_at = Column(TEXT, nullable=False)


# ─── agent_runs：每个 session 里每个 agent 的执行记录 ───
class AgentRun(Base):
    __tablename__ = "agent_runs"
    __table_args__ = (
        UniqueConstraint("session_id", "step_index"),
    )

    id = Column(INTEGER, primary_key=True, autoincrement=True)
    session_id = Column(TEXT, ForeignKey("sessions.session_id"), nullable=False)
    agent_name = Column(TEXT, nullable=False)                   # drize-engineer
    step_index = Column(INTEGER, nullable=False)               # 在 session 里的序号
    wave = Column(INTEGER, nullable=False, default=0)           # DAG wave：同 wave 并行
    input_json = Column(TEXT)                                   # 传给这个 agent 的完整 input
    output_json = Column(TEXT)                                  # 这个 agent 的原始输出
    llm_provider = Column(TEXT)                                  # openrouter/ollama/fallback
    llm_model = Column(TEXT)                                     # gpt-4o-mini / qwen2.5:7b
    status = Column(TEXT, nullable=False)                        # pending/running/completed/failed/skipped/approval_pending
    error_message = Column(TEXT)
    started_at = Column(TEXT)
    completed_at = Column(TEXT)
    duration_ms = Column(INTEGER)
    retry_count = Column(INTEGER, default=0)
    output_hash = Column(TEXT)                                   # SHA256(output_json)，用于幂等
    idempotency_key = Column(TEXT, nullable=False)               # SHA256(session_id + step_index + input_hash)
    updated_at = Column(TEXT)                                    # Day 9: retry_count 更新时同步更新


# ─── checkpoints：durability（MeshFlow 生产模式）───
class Checkpoint(Base):
    __tablename__ = "checkpoints"

    session_id = Column(TEXT, ForeignKey("sessions.session_id"), nullable=False, primary_key=True)
    step_id = Column(TEXT, nullable=False, primary_key=True)    # = agent_runs.id 的字符串
    step_index = Column(INTEGER, nullable=False)
    status = Column(TEXT, nullable=False)                        # STARTED/COMMITTED/SKIPPED
    input_hash = Column(TEXT)
    output_hash = Column(TEXT)
    output_data = Column(BLOB)                                   # gzip 压缩的 JSON
    started_at = Column(TEXT)
    committed_at = Column(TEXT)
    idempotency_key = Column(TEXT, nullable=False)


# ─── pipelines：用户保存的 DAG 模板 ───
class Pipeline(Base):
    __tablename__ = "pipelines"

    id = Column(INTEGER, primary_key=True, autoincrement=True)
    pipeline_id = Column(TEXT, unique=True, nullable=False)
    name = Column(TEXT, nullable=False)
    description = Column(TEXT)
    dag_json = Column(TEXT, nullable=False)                      # {"nodes":[...], "edges":[...]}
    created_at = Column(TEXT, nullable=False)
    updated_at = Column(TEXT, nullable=False)


# ─── approval_requests：HITL 中断审批门 ───
class ApprovalRequest(Base):
    __tablename__ = "approval_requests"

    id = Column(INTEGER, primary_key=True, autoincrement=True)
    session_id = Column(TEXT, ForeignKey("sessions.session_id"), nullable=False)
    run_id = Column(INTEGER, ForeignKey("agent_runs.id"), nullable=False)
    reason = Column(TEXT, nullable=False)                        # 为什么要审批
    request_payload = Column(TEXT)                               # 等审批的操作（JSON）
    status = Column(TEXT, nullable=False, default="pending")     # pending/approved/rejected/timeout
    approver = Column(TEXT)
    response_at = Column(TEXT)
    expires_at = Column(TEXT, nullable=False)                    # 审批超时时间（默认 30 分钟）
    created_at = Column(TEXT, nullable=False)


# ─── llm_config：每个 agent 绑定的 LLM 配置 ───
class LLMConfig(Base):
    __tablename__ = "llm_config"

    agent_name = Column(TEXT, primary_key=True)                  # drize-engineer
    provider = Column(TEXT, nullable=False)                      # openrouter/ollama/mock
    model = Column(TEXT, nullable=False)                          # gpt-4o-mini / qwen2.5:7b
    temperature = Column(REAL, default=0.7)
    max_tokens = Column(INTEGER, default=4096)
    fallback_provider = Column(TEXT)                              # 失败降级
    fallback_model = Column(TEXT)
    updated_at = Column(TEXT, nullable=False)


# ─── audit_log：全量审计 ───
class AuditLog(Base):
    __tablename__ = "audit_log"

    id = Column(INTEGER, primary_key=True, autoincrement=True)
    action = Column(TEXT, nullable=False)                        # SESSION_CREATE/RUN_COMPLETE/APPROVAL_APPROVED
    target_type = Column(TEXT, nullable=False)                    # session/run/checkpoint
    target_id = Column(TEXT, nullable=False)
    metadata_json = Column(TEXT)
    actor = Column(TEXT)                                          # user/system/csharp_host
    created_at = Column(TEXT, nullable=False)


# ============================================================
# 4 个索引（指南针 ④ L259-263）
# ============================================================

Index("idx_agent_runs_session", AgentRun.session_id)
Index("idx_agent_runs_status", AgentRun.status)
Index("idx_checkpoints_session", Checkpoint.session_id)
Index("idx_audit_log_time", AuditLog.created_at)


# ============================================================
# 初始化函数（Day 1 核心：create_all）
# ============================================================

def init_db() -> None:
    """幂等建表：C# 旧表不动，Python 7 张新表 + 4 索引 + 3 PRAGMA 自动就位"""
    Base.metadata.create_all(engine)
    print(f"✅ SQLite schema 对齐完成 → {DB_PATH}")
    with engine.connect() as conn:
        print(f"   PRAGMA journal_mode = {conn.execute(text('PRAGMA journal_mode')).scalar()}")


if __name__ == "__main__":
    init_db()
