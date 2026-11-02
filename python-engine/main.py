"""
FastAPI 主入口 · 端口 8765（指南针 D2）

API 端点（精确对齐指南针 L274-283）：
  GET  /health            ← C# 每 2s 探活
  POST /shutdown          ← 优雅关闭（保存 checkpoint）
  POST /api/chat/stream   ← 主 SSE 入口（四种调度模式）
  GET  /api/agents        ← AgentRegistry 合成结果
"""

from __future__ import annotations

# Phase 5 PyInstaller 打包修复：Windows 控制台默认 GBK，emoji 会炸
# 重配置 stdout/stderr 为 UTF-8（铁律 #24 只增量改）
import sys
try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

import asyncio
import json
import os
import signal
import sys
from contextlib import asynccontextmanager
from datetime import datetime
from typing import Optional

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field
from sqlalchemy import text
from sse_starlette.sse import EventSourceResponse

from db import engine, init_db
from agent_registry import AgentRegistry
from litellm_setup import MODE as LITELLM_MODE, configure_mock_fail
from scheduler import SchedulerEngine, abort_session, is_aborted
from hitl import hitl


# ============================================================
# 生命周期
# ============================================================

@asynccontextmanager
async def lifespan(app: FastAPI):
    print(f"\n🚀 Python Engine 启动 → {datetime.utcnow().isoformat()}Z")
    print(f"   port=8765 | litellm_mode={LITELLM_MODE}")

    # ===== Day 13: 启动时建 schema（C# 3 旧表 + Python 7 新表）=====
    _ensure_csharp_tables()   # agents + install_log + status_events
    init_db()                 # Python 7 张新表（sessions/agent_runs/checkpoints/...）
    _seed_agents_if_empty()   # 11 个专岗 seed（幂等）

    yield
    print(f"\n🛑 Python Engine 关闭 → {datetime.utcnow().isoformat()}Z")


# ============================================================
# Day 13: C# 3 表 + seed（和 TeamWorkbench Data/Database.cs + SeedService.cs 对齐）
# ============================================================

_CSHARP_CREATE_AGENTS = """
CREATE TABLE IF NOT EXISTS agents (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    cn_name TEXT NOT NULL,
    priority TEXT NOT NULL CHECK(priority IN ('P0','P1')),
    emoji TEXT DEFAULT '🤖',
    "desc" TEXT DEFAULT '',
    role TEXT DEFAULT '',
    how_to_trigger TEXT,
    process TEXT,
    boundary TEXT,
    tools TEXT,
    depends_on TEXT,
    install_status TEXT DEFAULT 'not-installed',
    install_date TEXT,
    runtime_status TEXT DEFAULT 'idle' CHECK(runtime_status IN ('idle','busy')),
    last_seen TEXT,
    updated_at TEXT
)
"""

_CSHARP_CREATE_INSTALL_LOG = """
CREATE TABLE IF NOT EXISTS install_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    agent_name TEXT NOT NULL,
    result TEXT NOT NULL CHECK(result IN ('success','failed')),
    detail TEXT,
    created_at TEXT NOT NULL
)
"""

_CSHARP_CREATE_STATUS_EVENTS = """
CREATE TABLE IF NOT EXISTS status_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    agent_name TEXT NOT NULL,
    event_type TEXT NOT NULL,
    event_data TEXT,
    created_at TEXT NOT NULL
)
"""

# 最小 seed：只插 id/name/cn_name/priority/emoji/desc（其他列用 DEFAULT）
_AGENT_SEED_SQL = """
INSERT OR IGNORE INTO agents (id, name, cn_name, priority, emoji, "desc", role, updated_at)
VALUES
    (1, 'drize-engineer',     '推演引擎',     'P0', '🧠', '改代码/花钱/不可逆前必调', '按 1号指令铁律 #22-32 执行全链路推演', datetime('now')),
    (2, 'arch-designer',      '架构师',       'P0', '🏗️', '新项目/大重构必调',        '从 01→02→03 层设计整体架构', datetime('now')),
    (3, 'project-taker',      '接手员',       'P0', '🤝', '新接手项目必调',            '快速搞懂陌生项目并产出开工包', datetime('now')),
    (4, 'ledger-keeper',      '台账登记员',   'P0', '📒', '开工/动手/收尾自动登记',     '保证所有操作有记录、每条记录有编号', datetime('now')),
    (5, 'rca-analyst',        'RCA 根因分析师','P1', '🔍', '出 bug 后五步根因→吸收',    '根因不许停在人为错误，必须产 RT-xx', datetime('now')),
    (6, 'competitor-surveyor','竞品调研员',   'P1', '🔭', '立项前 5 竞品实据含失败案', '编造竞品数据比没有竞品更严重', datetime('now')),
    (7, 'backend-expert',     '后端专家',     'P1', '⚙️', '写 API/改 DB/加鉴权',       '先想是不是一定要写，再选最高效的办法', datetime('now')),
    (8, 'safety-auditor',     '安全巡视员',   'P1', '🛡️', 'commit/发版前双检',         '安全与版本双检，版本号方向绝对不能反', datetime('now')),
    (9, 'report-writer',      '汇报出图员',   'P1', '📊', '对话收尾/里程碑自动出卡',   '严格遵循 report-template 出三层报告', datetime('now')),
    (10,'agents-builder',     'AGENTS.md 构建员','P1', '🛠️', '新项目自动建 AGENTS.md',   'AGENTS 只管设计层，执行层规矩不属于它', datetime('now')),
    (11,'mcp-finder',         'MCP 搜索员',   'P1', '🧩', '联网搜 4 来源找 MCP',       '找不到就说找不到，不许瞎编', datetime('now'));
"""


# ============================================================
# Day 17: pipelines 表（和 C# TeamWorkbench/Data/Database.cs 对齐）
# ============================================================

_CSHARP_CREATE_PIPELINES = """
CREATE TABLE IF NOT EXISTS pipelines (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    pipeline_id TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    description TEXT,
    dag_json TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
)
"""


def _ensure_csharp_tables() -> None:
    """C# 4 表幂等建表（Python 和 C# 共用同一个 SQLite）"""
    with engine.begin() as conn:
        conn.execute(text(_CSHARP_CREATE_AGENTS))
        conn.execute(text(_CSHARP_CREATE_INSTALL_LOG))
        conn.execute(text(_CSHARP_CREATE_STATUS_EVENTS))
        conn.execute(text(_CSHARP_CREATE_PIPELINES))
    print("✅ C# 4 表就绪（agents + install_log + status_events + pipelines）")


def _seed_agents_if_empty() -> None:
    """11 个专岗 seed（INSERT OR IGNORE，已存在不动）"""
    with engine.connect() as conn:
        count = conn.execute(text("SELECT COUNT(*) FROM agents")).scalar()
        if count and count > 0:
            print(f"   agents 表已有 {count} 行，跳过 seed")
            return
        conn.execute(text(_AGENT_SEED_SQL))
        conn.commit()
        count2 = conn.execute(text("SELECT COUNT(*) FROM agents")).scalar()
        print(f"   agents seed 完成 → {count2} 行")


app = FastAPI(title="TeamWorkbench Python Engine", version="0.1.0", lifespan=lifespan)

registry = AgentRegistry()
scheduler = SchedulerEngine(registry=registry)


# ============================================================
# 请求模型
# ============================================================

class ChatStreamRequest(BaseModel):
    mode: str = Field("DIRECT", description="DIRECT / SEQUENTIAL / PARALLEL / HYBRID")
    agent_names: list[str] = Field(..., min_length=1, description="要调度的 agent 英文名列表")
    prompt: str = Field(..., min_length=1, description="用户输入")


# ============================================================
# GET /health
# ============================================================

@app.get("/health")
async def health() -> dict:
    """C# 每 2s 探活（指南针 L274）"""
    db_ok = True
    try:
        with engine.connect() as conn:
            conn.execute(text("SELECT 1"))
    except Exception:
        db_ok = False

    litellm_ok = LITELLM_MODE in ("mock", "real")  # mock 也算 ok（至少初始化了）

    return {
        "status": "ok" if (db_ok and litellm_ok) else "degraded",
        "litellm_ok": litellm_ok,
        "db_ok": db_ok,
        "version": "0.1.0",
        "timestamp": datetime.utcnow().isoformat() + "Z",
    }


# ============================================================
# POST /shutdown
# ============================================================

@app.post("/shutdown")
async def shutdown() -> dict:
    """C# 优雅关闭（指南针 L275）"""
    # Day 5 checkpoint WAL 已经是幂等的，不需要额外保存
    print("POST /shutdown → 准备退出...")

    # 延迟一下让响应先返回
    async def _do_exit():
        await asyncio.sleep(0.5)
        os.kill(os.getpid(), signal.SIGTERM)

    asyncio.create_task(_do_exit())
    return {"status": "shutting_down"}


# ============================================================
# HITL 审批端点（指南针 L283）
# ============================================================

class ApprovalRequest(BaseModel):
    approved: bool = Field(..., description="True=批准 / False=拒绝")
    approver: str = Field("C#-Client", description="审批者标识")


@app.get("/api/approvals")
async def get_pending_approvals() -> list[dict]:
    """C# 前端拉待审批列表"""
    return hitl.get_pending()


@app.post("/api/approvals/{request_id}/respond")
async def respond_approval(request_id: int, req: ApprovalRequest) -> dict:
    """
    C# 前端点"批准/拒绝"

    用法：
      curl -X POST http://127.0.0.1:8765/api/approvals/7/respond \\
           -H "Content-Type: application/json" \\
           -d '{"approved": true}'
    """
    hitl.respond(request_id, approved=req.approved, approver=req.approver)
    return {"status": "ok", "request_id": request_id, "approved": req.approved}


# ============================================================
# POST /api/sessions/{session_id}/abort · 指南针 L282
# ============================================================

@app.post("/api/sessions/{session_id}/abort")
async def abort_session_endpoint(session_id: str) -> dict:
    """
    C# 点"停止"按钮 → 立即中断 + checkpoint 收尾

    用法：
      curl -X POST http://127.0.0.1:8765/api/sessions/xxx/abort
    """
    abort_session(session_id)
    # 同时更新 sessions 表 status=aborted
    with engine.begin() as conn:
        conn.execute(text("""
            UPDATE sessions SET status = 'aborted', completed_at = :ts
            WHERE session_id = :sid AND status NOT IN ('completed', 'failed')
        """), {"ts": datetime.utcnow().isoformat() + "Z", "sid": session_id})
    return {"status": "aborted", "session_id": session_id}


# ============================================================
# POST /api/mock/fail · Day 9: Mock 失败注入（verify_day9.py 用）
# ============================================================

class MockFailRequest(BaseModel):
    remaining: int = Field(..., ge=0, description="接下来 N 次 Mock 调用会抛 Exception")


@app.post("/api/mock/fail")
async def mock_fail_endpoint(req: MockFailRequest) -> dict:
    """
    Day 9: 设置 Mock 模式下接下来几次调用会失败

    用法：
      curl -X POST http://127.0.0.1:8765/api/mock/fail \
           -H "Content-Type: application/json" \
           -d '{"remaining": 3}'

    效果：接下来 3 次 call_llm_async Mock 调用抛 RuntimeError，
    第 4 次开始恢复正常。用于测试 scheduler._with_retry 指数退避全链路。
    """
    if LITELLM_MODE != "mock":
        return {"status": "ignored", "reason": f"当前 MODE={LITELLM_MODE} 不是 mock，忽略"}
    configure_mock_fail(req.remaining)
    return {"status": "ok", "remaining": req.remaining, "mode": LITELLM_MODE}


# ============================================================
# POST /api/sessions/{session_id}/resume · Day 7 新增
# ============================================================

@app.post("/api/sessions/{session_id}/resume")
async def resume_session_endpoint(session_id: str, request: Request):
    """
    Day 7: 从 checkpoint 恢复 session（SSE 流）

    规则（scheduler.resume_from 实现）：
      COMMITTED → 跳过，直接用已保存的 output
      STARTED / SKIPPED → 真重跑
      agent_runs.status='running' 但 checkpoint 全 COMMITTED
        → 自动修正为 'completed'（_auto_fix_run_status）

    用法：
      curl -N -X POST http://127.0.0.1:8765/api/sessions/xxx/resume

    Day 7 测试策略：
      ① 先完整跑 session → checkpoint 全 COMMITTED
      ② 手动改 checkpoint 表：step N 标 STARTED（模拟挂掉）
      ③ 调 resume → step N 真重跑，其余跳过
    """
    # 验证 session 存在
    with engine.connect() as conn:
        row = conn.execute(text(
            "SELECT session_id FROM sessions WHERE session_id = :sid"
        ), {"sid": session_id}).fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail=f"session {session_id} 不存在")

    last_event_id = request.headers.get("Last-Event-ID")
    if last_event_id:
        print(f"[resume] Last-Event-ID={last_event_id}")

    async def event_generator():
        start = datetime.utcnow()
        seq = 0

        def _yield_with_id(event_name: str, data_json: str):
            nonlocal seq
            eid = f"{session_id}-{seq}"
            seq += 1
            return {"event": event_name, "data": data_json, "id": eid}

        try:
            yield _yield_with_id("run_started", json.dumps({
                "session_id": session_id,
                "mode": "resume",
                "timestamp": datetime.utcnow().isoformat() + "Z",
            }))

            events, sid = await scheduler.resume_from(session_id)

            for ev in events:
                if await request.is_disconnected():
                    print(f"  🛑 client disconnected during resume")
                    yield _yield_with_id("aborted", json.dumps({
                        "session_id": session_id,
                        "reason": "client_disconnected",
                        "timestamp": datetime.utcnow().isoformat() + "Z",
                    }))
                    return

                payload = {
                    "run_id": ev.run_id,
                    "agent_name": ev.agent_name,
                    "step_index": ev.step_index,
                    "wave": ev.wave,
                    "output": (ev.output or "")[:200],
                    "session_id": sid,
                    "timestamp": ev.timestamp,
                }
                if ev.error:
                    payload["error"] = ev.error
                if ev.extra:
                    payload.update(ev.extra)
                yield _yield_with_id(ev.event, json.dumps(payload, ensure_ascii=False))

            duration_ms = int((datetime.utcnow() - start).total_seconds() * 1000)
            yield _yield_with_id("session_complete", json.dumps({
                "session_id": sid,
                "mode": "resume",
                "total_duration_ms": duration_ms,
                "summary": f"resume {sid} 完成，耗时 {duration_ms}ms",
                "timestamp": datetime.utcnow().isoformat() + "Z",
            }))

        except Exception as exc:
            yield _yield_with_id("error", json.dumps({
                "code": type(exc).__name__.upper(),
                "message": str(exc),
                "timestamp": datetime.utcnow().isoformat() + "Z",
            }))

    return EventSourceResponse(
        event_generator(),
        media_type="text/event-stream",
        ping=15,
    )


# ============================================================
# GET /api/agents
# ============================================================

@app.get("/api/agents")
async def get_agents() -> list[dict]:
    """C# 初始化时调用（指南针 L277）"""
    agents = registry.load_all()
    return [
        {
            "name": a.name,
            "cn_name": a.cn_name,
            "priority": a.priority,
            "install_status": a.install_status,
            "runtime_status": a.runtime_status,
            "has_system_prompt": bool(a.system_prompt),
        }
        for a in agents
    ]


# ============================================================
# POST /api/pipelines/run · Day 17：一键跑 DAG 模板
# ============================================================

class PipelineRunRequest(BaseModel):
    pipeline_id: str = Field(..., description="pipelines 表的 pipeline_id 字段")
    prompt: str = Field(..., min_length=1, description="用户输入")


def _derive_mode_from_dag(nodes: list, edges: list) -> str:
    """从 dag_json 自动推导调度模式"""
    if edges and len(edges) > 0:
        return "HYBRID"       # 有依赖 → DAG 分层
    if len(nodes) > 1:
        return "PARALLEL"     # 多 agent 无依赖 → 真并行
    return "DIRECT"          # 单 agent


@app.post("/api/pipelines/run")
async def pipeline_run(request: Request, req: PipelineRunRequest):
    """
    Day 17: 一键跑 C# 保存的 DAG 模板（指南针 L279）

    用法：
      curl -N -X POST http://127.0.0.1:8765/api/pipelines/run \\
           -H "Content-Type: application/json" \\
           -d '{"pipeline_id":"pipe_xxx","prompt":"执行这个流水线"}'

    内部流程：
      1) 读 pipelines 表 dag_json → nodes + edges
      2) edges 非空 → HYBRID；多节点无 edges → PARALLEL；单节点 → DIRECT
      3) agent_names = nodes[].name
      4) scheduler.run(mode, agent_names, prompt) → SSE 流
    """
    # ① 读模板
    with engine.connect() as conn:
        row = conn.execute(text(
            "SELECT dag_json, name FROM pipelines WHERE pipeline_id = :pid"
        ), {"pid": req.pipeline_id}).fetchone()
        if row is None:
            raise HTTPException(status_code=404, detail=f"pipeline {req.pipeline_id} 不存在")

    dag_json = json.loads(row[0])
    template_name = row[1] or req.pipeline_id
    nodes = dag_json.get("nodes", [])
    edges = dag_json.get("edges", [])

    if not nodes:
        raise HTTPException(status_code=400, detail="模板 nodes 为空，没有可跑的 agent")

    agent_names = [n["name"] for n in nodes]
    mode_upper = _derive_mode_from_dag(nodes, edges)

    # ② 验证 agent 存在
    agents = registry.load_all()
    valid_names = {a.name for a in agents}
    missing = [n for n in agent_names if n not in valid_names]
    if missing:
        raise HTTPException(status_code=400, detail=f"模板里找不到 agent: {missing}")

    print(f"[pipeline_run] template={template_name} mode={mode_upper} "
          f"agents={agent_names} edges={len(edges)}")

    # ③ SSE 流（复用 chat_stream 的 event_generator 结构）
    async def event_generator():
        start = datetime.utcnow()
        session_id = None
        seq = 0

        def _yield_with_id(event_name: str, data_json: str):
            nonlocal seq
            eid = f"{session_id or 'creating'}-{seq}"
            seq += 1
            return {"event": event_name, "data": data_json, "id": eid}

        try:
            yield _yield_with_id("run_started", json.dumps({
                "event": "run_started",
                "session_id": "(creating...)",
                "mode": mode_upper,
                "source": "pipeline",
                "pipeline_id": req.pipeline_id,
                "pipeline_name": template_name,
                "agent_count": len(agent_names),
                "timestamp": datetime.utcnow().isoformat() + "Z",
            }))

            events, session_id = await scheduler.run(mode_upper, agent_names, req.prompt)
            seq = 1

            for ev in events:
                if await request.is_disconnected():
                    yield _yield_with_id("aborted", json.dumps({
                        "session_id": session_id, "reason": "client_disconnected",
                        "timestamp": datetime.utcnow().isoformat() + "Z",
                    }))
                    return

                if session_id and is_aborted(session_id):
                    yield _yield_with_id("aborted", json.dumps({
                        "session_id": session_id, "reason": "abort_requested",
                        "timestamp": datetime.utcnow().isoformat() + "Z",
                    }))
                    return

                payload = {
                    "event": ev.event,
                    "run_id": ev.run_id,
                    "agent_name": ev.agent_name,
                    "step_index": ev.step_index,
                    "wave": ev.wave,
                    "output": ev.output[:200] if ev.output else "",
                    "session_id": session_id,
                    "timestamp": ev.timestamp,
                }
                if ev.error:
                    payload["error"] = ev.error
                if ev.extra:
                    payload.update(ev.extra)
                yield _yield_with_id(ev.event, json.dumps(payload, ensure_ascii=False))

            duration_ms = int((datetime.utcnow() - start).total_seconds() * 1000)
            yield _yield_with_id("session_complete", json.dumps({
                "event": "session_complete",
                "session_id": session_id,
                "mode": mode_upper,
                "source": "pipeline",
                "pipeline_id": req.pipeline_id,
                "total_duration_ms": duration_ms,
                "timestamp": datetime.utcnow().isoformat() + "Z",
            }))

        except asyncio.CancelledError:
            yield _yield_with_id("aborted", json.dumps({
                "event": "aborted", "session_id": session_id or "(unknown)",
                "reason": "abort_requested",
                "timestamp": datetime.utcnow().isoformat() + "Z",
            }))
        except Exception as exc:
            yield _yield_with_id("error", json.dumps({
                "event": "error", "code": type(exc).__name__.upper(),
                "message": str(exc),
                "timestamp": datetime.utcnow().isoformat() + "Z",
            }))

    return EventSourceResponse(
        event_generator(),
        media_type="text/event-stream",
        ping=15,
    )


# ============================================================
# POST /api/chat/stream · SSE 主入口
# ============================================================

@app.post("/api/chat/stream")
async def chat_stream(request: Request, req: ChatStreamRequest):
    """
    SSE 流（指南针 L276 + L285-302）

    用法：
      curl -N -X POST http://127.0.0.1:8765/api/chat/stream \\
           -H "Content-Type: application/json" \\
           -d '{"mode":"DIRECT","agent_names":["drize-engineer"],"prompt":"测试"}'
    """
    # ── Day 6: 读 Last-Event-ID header（断线重连用） ──
    last_event_id = request.headers.get("Last-Event-ID")
    if last_event_id:
        print(f"[SSE] 客户端带 Last-Event-ID={last_event_id} → 断线重连请求")

    # 验证 mode
    mode_upper = req.mode.upper()
    supported = ("DIRECT", "SEQUENTIAL", "PARALLEL", "HYBRID")
    if mode_upper not in supported:
        raise HTTPException(status_code=400, detail=f"不支持的 mode: {req.mode}（支持 {supported}）")

    # 验证 agent_names 存在
    agents = registry.load_all()
    valid_names = {a.name for a in agents}
    missing = [n for n in req.agent_names if n not in valid_names]
    if missing:
        raise HTTPException(status_code=400, detail=f"找不到 agent: {missing}")

    async def event_generator():
        start = datetime.utcnow()
        session_id = None
        seq = 0  # Day 6: SSE event 递增序号

        # ── 辅助：加 id 的 yield wrapper ──
        def _yield_with_id(event_name: str, data_json: str):
            nonlocal seq
            eid = f"{session_id or 'creating'}-{seq}"
            seq += 1
            return {
                "event": event_name,
                "data": data_json,
                "id": eid,
            }

        try:
            # run_started
            yield _yield_with_id("run_started", json.dumps({
                "event": "run_started",
                "session_id": "(creating...)",
                "mode": mode_upper,
                "agent_count": len(req.agent_names),
                "timestamp": datetime.utcnow().isoformat() + "Z",
            }))

            # 调调度器（内部会写 sessions + agent_runs + checkpoint）
            events, session_id = await scheduler.run(mode_upper, req.agent_names, req.prompt)
            # 分配了 session_id 后，id 序列重新对齐
            seq = 1  # 上一个 run_started 已经用了 seq=0

            # agent_started / agent_completed / approval_pending / agent_skipped
            for ev in events:
                # ① 客户端断连检查
                if await request.is_disconnected():
                    print(f"  🛑 client disconnected → session {session_id}")
                    yield _yield_with_id("aborted", json.dumps({
                        "session_id": session_id,
                        "reason": "client_disconnected",
                        "timestamp": datetime.utcnow().isoformat() + "Z",
                    }))
                    return

                # ② POST /api/sessions/{id}/abort 检查
                if session_id and is_aborted(session_id):
                    print(f"  🛑 abort signal received → session {session_id}")
                    yield _yield_with_id("aborted", json.dumps({
                        "session_id": session_id,
                        "reason": "abort_requested",
                        "timestamp": datetime.utcnow().isoformat() + "Z",
                    }))
                    return

                payload = {
                    "event": ev.event,
                    "run_id": ev.run_id,
                    "agent_name": ev.agent_name,
                    "step_index": ev.step_index,
                    "wave": ev.wave,
                    "output": ev.output[:200] if ev.output else "",
                    "session_id": session_id,
                    "timestamp": ev.timestamp,
                }
                if ev.error:
                    payload["error"] = ev.error
                if ev.extra:
                    payload.update(ev.extra)
                yield _yield_with_id(ev.event, json.dumps(payload, ensure_ascii=False))

            # session_complete
            duration_ms = int((datetime.utcnow() - start).total_seconds() * 1000)
            yield _yield_with_id("session_complete", json.dumps({
                "event": "session_complete",
                "session_id": session_id,
                "mode": mode_upper,
                "agent_count": len(req.agent_names),
                "total_duration_ms": duration_ms,
                "summary": f"session {session_id} 完成，耗时 {duration_ms}ms",
                "timestamp": datetime.utcnow().isoformat() + "Z",
            }))

        except asyncio.CancelledError:
            yield _yield_with_id("aborted", json.dumps({
                "event": "aborted",
                "session_id": session_id or "(unknown)",
                "reason": "abort_requested",
                "timestamp": datetime.utcnow().isoformat() + "Z",
            }))
        except Exception as exc:
            yield _yield_with_id("error", json.dumps({
                "event": "error",
                "code": type(exc).__name__.upper(),
                "message": str(exc),
                "timestamp": datetime.utcnow().isoformat() + "Z",
            }))

    return EventSourceResponse(
        event_generator(),
        media_type="text/event-stream",
        ping=15,  # 指南针 L677：sse-starlette ping=15s
    )


# ============================================================
# 启动入口
# ============================================================

if __name__ == "__main__":
    import uvicorn
    # Phase 5 PyInstaller 修复：传 app 对象而非字符串 "main:app"
    # （PyInstaller 打包后模块名变了，字符串 import 会炸）
    uvicorn.run(app, host="127.0.0.1", port=8765, reload=False)
