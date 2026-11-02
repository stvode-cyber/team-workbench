"""
Scheduler Engine · DIRECT / SEQ / PARALLEL / HYBRID 四种模式

精确对齐指南针 ⑥ L335-412（全部四种模式现已实现！）：
  HYBRID = graphlib.TopologicalSorter 分层 + 每层 asyncio.gather 并行 + context 波间传递

Day 4: DIRECT/SEQ/PARALLEL
Day 5: checkpoint WAL + resume_from
Day 7: HYBRID DAG + TopologicalSorter 分层
"""

from __future__ import annotations

import asyncio
import graphlib
import json
import uuid
from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional

from sqlalchemy import text

from db import engine
from agent_registry import AgentRegistry, AgentDefinition
from litellm_setup import call_llm_async, DEFAULT_MODEL, MODE
from checkpointer import Checkpointer
from hitl import hitl, HITLManager


# ============================================================
# Abort 全局 registry（session_id → asyncio.Event）
# ============================================================

_abort_events: dict[str, asyncio.Event] = {}


def register_session(session_id: str) -> asyncio.Event:
    """session 开始时注册 abort Event"""
    _abort_events[session_id] = asyncio.Event()
    return _abort_events[session_id]


def abort_session(session_id: str) -> None:
    """POST /api/sessions/{id}/abort 端点调这个"""
    ev = _abort_events.get(session_id)
    if ev:
        ev.set()
        print(f"  🛑 abort_session: {session_id} 已设置")


def is_aborted(session_id: str) -> bool:
    ev = _abort_events.get(session_id)
    return ev.is_set() if ev else False


# ============================================================
# 事件（SSE 层 Day 6 会接这个）
# ============================================================

@dataclass
class SchedulerEvent:
    """模拟 SSE event（Day 6 会改成 async generator yield SSE 字符串）"""
    event: str                     # agent_started / agent_completed / agent_failed / approval_pending
    agent_name: str
    step_index: int = 0
    wave: int = 0
    output: str = ""
    error: str = ""
    llm_model: str = ""
    run_id: str = ""
    session_id: str = ""
    timestamp: str = field(default_factory=lambda: datetime.utcnow().isoformat() + "Z")
    extra: dict = field(default_factory=dict)  # approval_id / 其他扩展字段


# ============================================================
# 辅助：写 DB run
# ============================================================

def _now_iso() -> str:
    return datetime.utcnow().isoformat() + "Z"


def _create_session(
    mode: str,
    input_prompt: str,
    agent_names: Optional[list[str]] = None,
    layers: Optional[list[list[int]]] = None,
) -> str:
    """写 sessions 表 → 返回 session_id → 注册 abort Event"""
    session_id = str(uuid.uuid4())

    # Day 7: input_json 加 agent_names + layers，
    # 让 resume_from() 能重建 session 上下文
    input_obj: dict = {"prompt": input_prompt}
    if agent_names is not None:
        input_obj["agent_names"] = agent_names
    if layers is not None:
        input_obj["layers"] = layers

    with engine.begin() as conn:
        conn.execute(text("""
            INSERT INTO sessions (session_id, mode, status, input_json, created_at)
            VALUES (:sid, :mode, 'running', :inp, :ts)
        """), {
            "sid": session_id,
            "mode": mode,
            "inp": json.dumps(input_obj),
            "ts": _now_iso(),
        })
    register_session(session_id)
    return session_id


def _create_agent_run(
    session_id: str,
    agent: AgentDefinition,
    step_index: int,
    wave: int,
    input_json: str,
    llm_model: str,
) -> tuple[str, int]:
    """写 agent_runs 表（status=pending）→ 返回 (run_id_str, db_id_int)"""
    run_id_str = f"{session_id}::step{step_index}"
    with engine.begin() as conn:
        result = conn.execute(text("""
            INSERT OR REPLACE INTO agent_runs
            (session_id, agent_name, step_index, wave, input_json, llm_provider,
             llm_model, status, idempotency_key, retry_count)
            VALUES (:sid, :an, :si, :wv, :inp, 'openrouter', :model,
                    'pending', :idem, 0)
        """), {
            "sid": session_id,
            "an": agent.name,
            "si": step_index,
            "wv": wave,
            "inp": input_json,
            "model": llm_model,
            "idem": run_id_str,
        })
    db_id = result.lastrowid or 0
    return run_id_str, db_id


def _update_run_status(session_id: str, step_index: int, status: str, output: str = "") -> None:
    """agent_runs.status → running/completed/failed（用 started_at / completed_at）"""
    now = _now_iso()
    with engine.begin() as conn:
        if status == "running":
            conn.execute(text("""
                UPDATE agent_runs SET status = :s, started_at = :ts
                WHERE session_id = :sid AND step_index = :si
            """), {"s": status, "sid": session_id, "si": step_index, "ts": now})
        elif output:
            conn.execute(text("""
                UPDATE agent_runs SET status = :s, output_json = :o, completed_at = :ts,
                                     output_hash = :h
                WHERE session_id = :sid AND step_index = :si
            """), {
                "s": status,
                "o": json.dumps({"output": output}),
                "h": str(hash(output))[:16],
                "sid": session_id, "si": step_index, "ts": now,
            })
        else:
            conn.execute(text("""
                UPDATE agent_runs SET status = :s, completed_at = :ts
                WHERE session_id = :sid AND step_index = :si
            """), {"s": status, "sid": session_id, "si": step_index, "ts": now})


# ============================================================
# SchedulerEngine
# ============================================================

class SchedulerEngine:
    """四种调度模式 + checkpoint + resume_from + HYBRID DAG 分层"""

    def __init__(self, registry: Optional[AgentRegistry] = None) -> None:
        self.registry = registry or AgentRegistry()
        self.checkpointer = Checkpointer()

    # ── DAG 分层（graphlib.TopologicalSorter）────────────

    def build_layers(self, agents: list[AgentDefinition]) -> list[list[int]]:
        """
        从 depends_on 列构建 DAG → 拓扑分层 → 返回 wave-based layers。

        graphlib.TopologicalSorter 用法：
          static_order() 是扁平化逐个输出
          要分层必须 get_ready() + done() 循环（每波取出"所有依赖已满足的 node"）
        """
        name_to_idx = {a.name: i for i, a in enumerate(agents)}
        graph: dict[int, set[int]] = {}
        for i, agent in enumerate(agents):
            deps = set()
            for dep_name in agent.depends_on:
                if dep_name in name_to_idx:
                    deps.add(name_to_idx[dep_name])
            graph[i] = deps

        sorter = graphlib.TopologicalSorter(graph)
        sorter.prepare()

        layers: list[list[int]] = []
        while True:
            ready = sorter.get_ready()
            if not ready:
                break
            layer = sorted(ready)  # 同波按 index 排序
            layers.append(layer)
            sorter.done(*layer)

        return layers

    # ── 顶层 dispatch ──

    async def run(
        self,
        mode: str,
        agent_names: list[str],
        prompt: str,
        layers: Optional[list[list[int]]] = None,
    ) -> tuple[list[SchedulerEvent], str]:
        """按 mode 分发 → 返回 (事件列表, session_id)。HYBRID 可手动传 layers"""
        agents = self.registry.load_all()
        selected = [a for a in agents if a.name in agent_names]
        if not selected:
            raise ValueError(f"找不到 agent: {agent_names}")

        mode = mode.upper()
        print(f"\n{'═'*50}")
        print(f"[Scheduler] mode={mode} | agents={[a.name for a in selected]}")
        print(f"{'═'*50}")

        if mode == "HYBRID":
            actual_layers = layers or self.build_layers(selected)
            print(f"  HYBRID layers={actual_layers}")
            return await self._run_hybrid(selected, actual_layers, prompt)

        dispatch = {
            "DIRECT": self._run_direct,
            "SEQUENTIAL": self._run_sequential,
            "PARALLEL": self._run_parallel,
        }
        if mode not in dispatch:
            raise ValueError(f"未知模式: {mode}（支持 DIRECT/SEQUENTIAL/PARALLEL/HYBRID）")

        return await dispatch[mode](selected, prompt)

    # ── 三种模式（精确对齐指南针伪代码）────────────────────

    # ── Day 10 统一 HITL 审批门（指南针 D7 · 四模式全覆盖）──

    async def _check_approval(
        self,
        agent: AgentDefinition,
        session_id: str,
        run_id_str: str,
        run_db_id: int,
        prompt: str,
        events: list[SchedulerEvent],
        step_index: int,
        wave: int,
    ) -> str:
        """
        四模式 agent 执行前统一调。

        返回值：
          "approved" → 可以继续执行
          "rejected" / "timeout" → 要跳过该 agent
        """
        if not agent.approval_required:
            return "approved"

        # 创建审批请求 + 发 approval_pending（C# DispatchCenterView 要接这个事件）
        request_id = hitl.create_approval(
            session_id=session_id,
            run_id=run_db_id,
            reason=f"agent {agent.name} 需要人类审批",
            request_payload=json.dumps({"agent": agent.name, "prompt": prompt[:200]}),
        )
        events.append(SchedulerEvent(
            "approval_pending", agent.name, step_index, wave,
            run_id=run_id_str, session_id=session_id,
            extra={"approval_id": request_id},
        ))
        print(f"  ⏸ HITL 审批门: {agent.name} → request_id={request_id}")

        # Day 13: mock 模式自动 approve（C# 端冒烟时不等 30min）
        if MODE == "mock":
            print(f"  🧪 Mock 模式 → 自动 approve {agent.name}")
            hitl.respond(request_id, approved=True)
            return "approved"

        result = await hitl.wait_for_approval(
            request_id,
            abort_check=lambda: is_aborted(session_id),
        )
        print(f"  🎯 HITL 结果: {agent.name} → {result}")
        return result

    # ── 四种调度模式 ──

    async def _run_direct(self, agents: list[AgentDefinition], prompt: str) -> tuple[list[SchedulerEvent], str]:
        """L335-348：单 agent 直调 + checkpoint WAL"""
        session_id = _create_session("DIRECT", prompt, agent_names=[a.name for a in agents])
        events: list[SchedulerEvent] = []

        agent = agents[0]
        step_id = f"{session_id}::step0"
        run_id, run_db_id = _create_agent_run(
            session_id, agent, step_index=0, wave=0,
            input_json=json.dumps({"prompt": prompt}),
            llm_model=DEFAULT_MODEL,
        )

        # checkpoint STARTED
        self.checkpointer.save_step_started(
            session_id, step_id, step_index=0,
            input_data=prompt, idempotency_key=step_id,
        )

        events.append(SchedulerEvent("agent_started", agent.name, 0, 0,
                                     run_id=run_id, session_id=session_id, llm_model=DEFAULT_MODEL))
        print(f"  🟢 started: {agent.name}")

        # ── Day 10: 统一 HITL 审批门 ─────────────────────────
        hitl_result = await self._check_approval(
            agent, session_id, run_id, run_db_id, prompt, events, step_index=0, wave=0,
        )
        if hitl_result != "approved":
            _update_run_status(session_id, 0, "skipped")
            events.append(SchedulerEvent(
                "agent_skipped", agent.name, 0, 0,
                run_id=run_id, session_id=session_id,
                error=f"HITL {hitl_result}",
            ))
            print(f"  ⏭ HITL {hitl_result}: 跳过 {agent.name}")
            self._close_session(session_id, "completed")
            return events, session_id

        _update_run_status(session_id, 0, "running")

        try:
            output = await self._with_retry(
                lambda: call_llm_async(
                    model=DEFAULT_MODEL,
                    system_prompt=agent.system_prompt,
                    user_prompt=prompt,
                ),
                step_index=0, session_id=session_id,
            )
        except asyncio.CancelledError:
            # abort → 收尾后往上层抛（Day 11: 补 agent_runs.status=aborted）
            _update_run_status(session_id, 0, "aborted")
            self._handle_abort(session_id, events)
            raise
        except Exception as exc:
            # 3 次全失败 → 标记 failed，发 agent_failed 事件
            _update_run_status(session_id, 0, "failed")
            events.append(SchedulerEvent(
                "agent_failed", agent.name, 0, 0,
                run_id=run_id, session_id=session_id,
                error=str(exc),
            ))
            print(f"  ❌ failed: {agent.name} → {exc}")
            self._close_session(session_id, "failed")
            return events, session_id

        # checkpoint COMMITTED
        self.checkpointer.save_step_committed(session_id, step_id, output)
        _update_run_status(session_id, 0, "completed", output)

        events.append(SchedulerEvent("agent_completed", agent.name, 0, 0,
                                     output=output, run_id=run_id, session_id=session_id,
                                     llm_model=DEFAULT_MODEL))
        print(f"  ✅ completed: {agent.name} ({len(output)} chars)")

        self._close_session(session_id, "completed")
        return events, session_id

    async def _run_sequential(self, agents: list[AgentDefinition], prompt: str) -> tuple[list[SchedulerEvent], str]:
        """L350-366：Agent A 输出 = Agent B 输入 + checkpoint WAL + Day 10 HITL + Day 9 try/except"""
        session_id = _create_session("SEQUENTIAL", prompt, agent_names=[a.name for a in agents])
        events: list[SchedulerEvent] = []
        prev_output = prompt  # 第一个 agent 吃原始 prompt

        for i, agent in enumerate(agents):
            step_id = f"{session_id}::step{i}"
            run_id, run_db_id = _create_agent_run(
                session_id, agent, step_index=i, wave=i,
                input_json=json.dumps({"prev_output": prev_output[-200:]}),
                llm_model=DEFAULT_MODEL,
            )

            # checkpoint STARTED
            self.checkpointer.save_step_started(
                session_id, step_id, step_index=i,
                input_data=prev_output, idempotency_key=step_id,
            )

            events.append(SchedulerEvent("agent_started", agent.name, i, i,
                                         run_id=run_id, session_id=session_id, llm_model=DEFAULT_MODEL))
            print(f"  🟢 [{i}] started: {agent.name} (wave={i})")

            # ── Day 10: HITL 审批门 ─────────────────────────
            hitl_result = await self._check_approval(
                agent, session_id, run_id, run_db_id, prev_output, events, step_index=i, wave=i,
            )
            if hitl_result != "approved":
                _update_run_status(session_id, i, "skipped")
                events.append(SchedulerEvent(
                    "agent_skipped", agent.name, i, i,
                    run_id=run_id, session_id=session_id,
                    error=f"HITL {hitl_result}",
                ))
                print(f"  ⏭ [{i}] HITL {hitl_result}: 跳过 {agent.name}")
                # SEQUENTIAL 某步被拒绝 → 后续 step 继续跑（不用 break）
                continue

            # ── Day 9: try/except 包裹 _with_retry ───────────
            _update_run_status(session_id, i, "running")
            try:
                output = await self._with_retry(
                    lambda: call_llm_async(
                        model=DEFAULT_MODEL,
                        system_prompt=agent.system_prompt,
                        user_prompt=prev_output,
                    ),
                    step_index=i, session_id=session_id,
                )
            except asyncio.CancelledError:
                _update_run_status(session_id, i, "aborted")
                self._handle_abort(session_id, events)
                raise
            except Exception as exc:
                _update_run_status(session_id, i, "failed")
                events.append(SchedulerEvent(
                    "agent_failed", agent.name, i, i,
                    run_id=run_id, session_id=session_id,
                    error=str(exc),
                ))
                print(f"  ❌ [{i}] failed: {agent.name} → {exc}")
                self._close_session(session_id, "failed")
                return events, session_id

            prev_output = output

            # checkpoint COMMITTED
            self.checkpointer.save_step_committed(session_id, step_id, output)

            _update_run_status(session_id, i, "completed", output)
            events.append(SchedulerEvent("agent_completed", agent.name, i, i,
                                         output=output, run_id=run_id, session_id=session_id,
                                         llm_model=DEFAULT_MODEL))
            print(f"  ✅ [{i}] completed: {agent.name} → 传给下一个 ({len(output)} chars)")

        self._close_session(session_id, "completed")
        return events, session_id

    async def _run_parallel(self, agents: list[AgentDefinition], prompt: str) -> tuple[list[SchedulerEvent], str]:
        """L368-378：asyncio.gather 同时跑 + Day 10 HITL"""
        session_id = _create_session("PARALLEL", prompt, agent_names=[a.name for a in agents])
        events: list[SchedulerEvent] = []

        # 先统一写 agent_runs pending
        run_info: list[tuple[str, int]] = []
        for i, agent in enumerate(agents):
            rid_str, rid_int = _create_agent_run(
                session_id, agent, step_index=i, wave=0,
                input_json=json.dumps({"prompt": prompt}),
                llm_model=DEFAULT_MODEL,
            )
            run_info.append((rid_str, rid_int))
            events.append(SchedulerEvent("agent_started", agent.name, i, 0,
                                         run_id=rid_str, session_id=session_id, llm_model=DEFAULT_MODEL))
            print(f"  🟢 started: {agent.name} (wave=0)")

        # 同时跑（_run_one 内部会调 HITL）
        # Day 12: gather 前 abort check + 外层 try/except
        if is_aborted(session_id):
            self._handle_abort(session_id, events)
            self._close_session(session_id, "aborted")
            return events, session_id
        try:
            results = await asyncio.gather(
                *[self._run_one(session_id, agent, i, prompt, run_info[i], events)
                  for i, agent in enumerate(agents)],
                return_exceptions=True,
            )
        except asyncio.CancelledError:
            self._handle_abort(session_id, events)
            raise

        for i, result in enumerate(results):
            agent = agents[i]
            rid_str = run_info[i][0]
            if isinstance(result, Exception):
                _update_run_status(session_id, i, "failed")
                events.append(SchedulerEvent("agent_failed", agent.name, i, 0,
                                              error=str(result), run_id=rid_str, session_id=session_id))
                print(f"  ❌ failed: {agent.name} → {result}")
            elif isinstance(result, str):
                # _run_one 返回 output_str（正常）或 "HITL_REJECTED" / "HITL_TIMEOUT"（跳过）
                if result.startswith("HITL_"):
                    _update_run_status(session_id, i, "skipped")
                    events.append(SchedulerEvent(
                        "agent_skipped", agent.name, i, 0,
                        run_id=rid_str, session_id=session_id,
                        error=f"HITL {result}",
                    ))
                    print(f"  ⏭ HITL {result}: 跳过 {agent.name}")
                else:
                    events.append(SchedulerEvent("agent_completed", agent.name, i, 0,
                                                  output=result, run_id=rid_str, session_id=session_id,
                                                  llm_model=DEFAULT_MODEL))
                    print(f"  ✅ completed: {agent.name} ({len(result)} chars)")
            else:
                # 未知返回值
                _update_run_status(session_id, i, "failed")
                events.append(SchedulerEvent("agent_failed", agent.name, i, 0,
                                              error=f"unknown result: {type(result)}",
                                              run_id=rid_str, session_id=session_id))

        self._close_session(session_id, "completed")
        return events, session_id

    async def _run_hybrid(
        self,
        agents: list[AgentDefinition],
        layers: list[list[int]],
        prompt: str,
    ) -> tuple[list[SchedulerEvent], str]:
        """
        L380-410：DAG 拓扑排序 + wave-based dispatch

        layers = [[0,1], [2], [3,4]]  # graphlib.TopologicalSorter 分层
        Layer 0: node 0,1 → 同波 asyncio.gather 并行
        Layer 1: node 2   → 等 Layer 0 全完再跑
        Layer 2: node 3,4 → 同波并行

        波间 context 传递：上一波 output 注入 context → 下一波 agent 能读
        """
        session_id = _create_session("HYBRID", prompt, agent_names=[a.name for a in agents], layers=layers)
        events: list[SchedulerEvent] = []
        context: dict[str, str] = {"initial_prompt": prompt}

        # 先统一写 agent_runs pending + checkpoint STARTED
        run_info_map: dict[int, tuple[str, int]] = {}
        for i, agent in enumerate(agents):
            step_id = f"{session_id}::step{i}"
            rid_str, rid_int = _create_agent_run(
                session_id, agent, step_index=i, wave=0,
                input_json=json.dumps({"prompt": prompt}),
                llm_model=DEFAULT_MODEL,
            )
            run_info_map[i] = (rid_str, rid_int)
            self.checkpointer.save_step_started(
                session_id, step_id, step_index=i,
                input_data=prompt, idempotency_key=step_id,
            )

        # 按 wave 分层跑
        for wave_idx, layer in enumerate(layers):
            print(f"\n  🌊 Wave {wave_idx}: nodes {layer}")

            # 同波并行
            tasks = []
            for node_idx in layer:
                agent = agents[node_idx]
                step_id = f"{session_id}::step{node_idx}"
                tasks.append(self._run_hybrid_single(
                    session_id=session_id,
                    agent=agent,
                    step_index=node_idx,
                    wave=wave_idx,
                    context=context,
                    step_id=step_id,
                    run_info=run_info_map[node_idx],
                    events=events,
                    initial_prompt=prompt,
                ))
                events.append(SchedulerEvent(
                    "agent_started", agent.name, node_idx, wave_idx,
                    run_id=step_id, session_id=session_id, llm_model=DEFAULT_MODEL,
                ))

            # Day 12: HYBRID gather 前 abort check + 外层 try/except
            if is_aborted(session_id):
                self._handle_abort(session_id, events)
                self._close_session(session_id, "aborted")
                return events, session_id
            try:
                results = await asyncio.gather(*tasks, return_exceptions=True)
            except asyncio.CancelledError:
                self._handle_abort(session_id, events)
                raise

            # 收集结果 → 注入 context + COMMITTED checkpoint
            for node_idx, result in zip(layer, results):
                agent = agents[node_idx]
                step_id = f"{session_id}::step{node_idx}"
                rid_str = run_info_map[node_idx][0]

                if isinstance(result, Exception):
                    _update_run_status(session_id, node_idx, "failed")
                    events.append(SchedulerEvent(
                        "agent_failed", agent.name, node_idx, wave_idx,
                        error=str(result), run_id=rid_str, session_id=session_id,
                    ))
                    print(f"  ❌ [{node_idx}] {agent.name} → {result}")
                elif isinstance(result, str) and result.startswith("HITL_"):
                    _update_run_status(session_id, node_idx, "skipped")
                    events.append(SchedulerEvent(
                        "agent_skipped", agent.name, node_idx, wave_idx,
                        run_id=rid_str, session_id=session_id,
                        error=f"HITL {result}",
                    ))
                    print(f"  ⏭ HITL {result}: 跳过 [{node_idx}] {agent.name}")
                else:
                    output = result if isinstance(result, str) else ""
                    context[agent.name] = output  # ← 波间传递！
                    self.checkpointer.save_step_committed(session_id, step_id, output)
                    _update_run_status(session_id, node_idx, "completed", output)
                    events.append(SchedulerEvent(
                        "agent_completed", agent.name, node_idx, wave_idx,
                        output=output, run_id=rid_str, session_id=session_id,
                        llm_model=DEFAULT_MODEL,
                    ))
                    print(f"  ✅ [{node_idx}] {agent.name} ({len(output)} chars)")

        self._close_session(session_id, "completed")
        return events, session_id

    async def _run_hybrid_single(
        self,
        session_id: str,
        agent: AgentDefinition,
        step_index: int,
        wave: int,
        context: dict[str, str],
        step_id: str,
        run_info: tuple[str, int],
        events: list[SchedulerEvent],
        initial_prompt: str,
    ) -> str:
        """HYBRID 模式单个 node 执行 + Day 10 HITL"""
        rid_str, rid_int = run_info

        # ── Day 10: HITL 审批门 ─────────────────────────
        hitl_result = await self._check_approval(
            agent, session_id, rid_str, rid_int, initial_prompt,
            events, step_index=step_index, wave=wave,
        )
        if hitl_result != "approved":
            return f"HITL_{hitl_result}"

        _update_run_status(session_id, step_index, "running")
        context_summary = "\n".join(
            f"{k}: {v[:200]}" for k, v in context.items()
        )
        return await self._with_retry(
            lambda: call_llm_async(
                model=DEFAULT_MODEL,
                system_prompt=agent.system_prompt,
                user_prompt=context_summary,
            ),
            step_index=step_index, session_id=session_id,
        )

    # ── Retry 指数退避 + 60s timeout（指南针 D8 + L412-425）──────

    async def _with_retry(
        self,
        coro_fn,
        max_retries: int = 3,
        timeout: float = 60.0,
        step_index: int = -1,
        session_id: str = "",
    ) -> str:
        """
        包装 call_llm_async：指数退避 3 次 + 60s timeout + abort 检查

        退避序列：1s, 2s, 4s（2 ** attempt）
        3 次全失败 → raise（上层 catch → agent_runs.status=failed）
        """
        last_exception: Optional[Exception] = None

        for attempt in range(max_retries):
            # ① POST /api/sessions/{id}/abort 检查
            if session_id and is_aborted(session_id):
                print(f"    🛑 abort detected before attempt {attempt + 1}")
                raise asyncio.CancelledError(f"session {session_id} aborted")

            try:
                print(f"    ↻ retry attempt {attempt + 1}/{max_retries}")
                return await asyncio.wait_for(coro_fn(), timeout=timeout)
            except asyncio.CancelledError:
                # abort 请求 → 直接往上抛，不 retry
                raise
            except Exception as exc:
                last_exception = exc
                print(f"    ✗ attempt {attempt + 1} failed: {type(exc).__name__}: {str(exc)[:80]}")

                # 更新 retry_count
                if session_id and step_index >= 0:
                    with engine.begin() as conn:
                        conn.execute(text("""
                            UPDATE agent_runs
                            SET retry_count = :rc, updated_at = :ts
                            WHERE session_id = :sid AND step_index = :si
                        """), {"rc": attempt + 1, "ts": _now_iso(), "sid": session_id, "si": step_index})

                if attempt < max_retries - 1:
                    wait = 2 ** attempt  # 1s, 2s, 4s
                    print(f"    ⏳ 指数退避 wait={wait}s (abort 每 0.25s 轮询)")
                    elapsed = 0.0
                    while elapsed < wait:
                        await asyncio.sleep(0.25)
                        elapsed += 0.25
                        if session_id and is_aborted(session_id):
                            print(f"    🛑 abort 打断退避等待 ({elapsed:.1f}s / {wait}s)")
                            raise asyncio.CancelledError(
                                f"session {session_id} aborted during backoff (waited {elapsed:.1f}s)"
                            )

        raise last_exception  # type: ignore[misc]

    # ── 内部：单个 agent 执行（给 PARALLEL gather 用 · 含 checkpoint）───────

    async def _run_one(
        self,
        session_id: str,
        agent: AgentDefinition,
        step_index: int,
        prompt: str,
        run_info: tuple[str, int],
        events: list[SchedulerEvent],
    ) -> str:
        """PARALLEL 模式单个 agent 执行 + Day 10 HITL"""
        step_id = f"{session_id}::step{step_index}"
        rid_str, rid_int = run_info

        # checkpoint STARTED
        self.checkpointer.save_step_started(
            session_id, step_id, step_index=step_index,
            input_data=prompt, idempotency_key=step_id,
        )

        # ── Day 10: HITL 审批门 ─────────────────────────
        hitl_result = await self._check_approval(
            agent, session_id, rid_str, rid_int, prompt,
            events, step_index=step_index, wave=0,
        )
        if hitl_result != "approved":
            return f"HITL_{hitl_result}"

        _update_run_status(session_id, step_index, "running")
        output = await self._with_retry(
            lambda: call_llm_async(
                model=DEFAULT_MODEL,
                system_prompt=agent.system_prompt,
                user_prompt=prompt,
            ),
            step_index=step_index, session_id=session_id,
        )

        # checkpoint COMMITTED
        self.checkpointer.save_step_committed(session_id, step_id, output)

        return output

    # ── 恢复（指南针 C7 · Day 7 完整集成 HYBRID）────────

    async def resume_from(self, session_id: str) -> tuple[list[SchedulerEvent], str]:
        """
        从 checkpoint 恢复 session（四种 mode 全支持）

        规则：
          COMMITTED → 跳过，直接用已保存的 output
          STARTED / SKIPPED → 真重跑
          agent_runs.status='running' 但 checkpoint 全 COMMITTED
            → 自动修正为 'completed'（Day 8 生产化）

        HYBRID 特别处理：
          从 sessions.input_json 拿 agent_names + layers 重建 DAG 拓扑
          已 COMMITTED 的 step output 注入 context → 下一波 agent 能读
          未 COMMITTED 的 step 按 wave 分层 gather 并行重跑
        """
        print(f"\n🔁 恢复 session: {session_id}")
        # Day 12 补：resume 进来也要注册 abort Event，
        # 否则 abort_session() 端点对 resume 中的 session 无效
        register_session(session_id)

        # ── 1. 查 session 基础信息 ──
        with engine.connect() as conn:
            sess_row = conn.execute(text("""
                SELECT mode, input_json FROM sessions
                WHERE session_id = :sid
            """), {"sid": session_id}).fetchone()
            if sess_row is None:
                raise ValueError(f"session {session_id} 不存在")
            mode = sess_row[0].upper()
            input_obj = json.loads(sess_row[1] or "{}")
            original_prompt = input_obj.get("prompt", "")
            saved_agent_names: list[str] = input_obj.get("agent_names", [])
            saved_layers: Optional[list[list[int]]] = input_obj.get("layers")

        # ── 2. 查 agent_runs 重建 agent 列表 ──
        with engine.connect() as conn:
            run_rows = conn.execute(text("""
                SELECT agent_name, step_index, wave, status
                FROM agent_runs
                WHERE session_id = :sid
                ORDER BY step_index
            """), {"sid": session_id}).fetchall()

        run_by_idx: dict[int, tuple[str, int, str]] = {}
        for row in run_rows:
            run_by_idx[row[1]] = (row[0], row[2], row[3])  # (agent_name, wave, status)

        # agent 列表（按 step_index 排序）
        agents_all = self.registry.load_all()
        name_to_def = {a.name: a for a in agents_all}
        agents: list[AgentDefinition] = []
        for idx in sorted(run_by_idx.keys()):
            agent_name = run_by_idx[idx][0]
            if agent_name in name_to_def:
                agents.append(name_to_def[agent_name])
        # 如果 agent_runs 表空，退回 session 里存的 agent_names
        if not agents and saved_agent_names:
            agents = [name_to_def[n] for n in saved_agent_names if n in name_to_def]

        # ── 3. 查 checkpoint 状态 ──
        steps = self.checkpointer.get_all_steps(session_id)
        committed_idx: set[int] = {
            s["step_index"] for s in steps if s["status"] == "COMMITTED"
        }
        print(f"  mode={mode} | agents={[a.name for a in agents]}")
        print(f"  checkpoint 记录: {len(steps)} 条 | COMMITTED={len(committed_idx)}")

        # ── 4. agent_runs 脏状态自动修正（Day 8 生产化） ──
        self._auto_fix_run_status(session_id, committed_idx)

        # ── 4b. Day 12: 查 agent_runs rowid（给 resume 的 _check_approval 用）──
        run_id_by_idx: dict[int, int] = {}
        with engine.connect() as conn:
            rid_rows = conn.execute(text("""
                SELECT step_index, rowid FROM agent_runs
                WHERE session_id = :sid
            """), {"sid": session_id}).fetchall()
            run_id_by_idx = {r[0]: r[1] for r in rid_rows}

        # ── 5. 按 mode 分支恢复 ──
        events: list[SchedulerEvent] = []

        if mode == "HYBRID":
            # ── HYBRID DAG 恢复：按 wave 分层 ──
            layers = saved_layers or self.build_layers(agents)
            context: dict[str, str] = {"initial_prompt": original_prompt}

            # 先把已 COMMITTED 的 output 全塞进 context（重建波间传递）
            for idx in sorted(committed_idx):
                step_id = f"{session_id}::step{idx}"
                output = self.checkpointer.get_committed_output(session_id, step_id)
                if idx < len(agents) and output:
                    context[agents[idx].name] = output

            print(f"  HYBRID layers={layers}")
            print(f"  恢复 context keys={list(context.keys())}")

            for wave_idx, layer in enumerate(layers):
                print(f"\n  🌊 Wave {wave_idx}: nodes {layer}")
                wave_tasks = []
                wave_events: list[SchedulerEvent] = []

                for node_idx in layer:
                    agent = agents[node_idx]
                    step_id = f"{session_id}::step{node_idx}"

                    if node_idx in committed_idx:
                        # 已 COMMITTED → 跳过，直接发 completed 事件
                        output = self.checkpointer.get_committed_output(session_id, step_id)
                        print(f"  ⏭ [{node_idx}] {agent.name} → 已 COMMITTED → 跳过")
                        wave_events.append(SchedulerEvent(
                            "agent_completed", agent.name, node_idx, wave_idx,
                            output=output or "(empty)",
                            run_id=step_id, session_id=session_id,
                        ))
                        context[agent.name] = output or ""
                    else:
                        # 未 COMMITTED → 真重跑
                        print(f"  🔄 [{node_idx}] {agent.name} → {run_by_idx.get(node_idx, (None,None,'?'))[2]} → 重跑")
                        wave_events.append(SchedulerEvent(
                            "agent_started", agent.name, node_idx, wave_idx,
                            run_id=step_id, session_id=session_id,
                        ))
                        wave_tasks.append(self._run_hybrid_single(
                            session_id=session_id,
                            agent=agent,
                            step_index=node_idx,
                            wave=wave_idx,
                            context=context,
                            step_id=step_id,
                        ))

                # 先把 started 事件发出去
                events.extend([e for e in wave_events if e.event == "agent_started"])

                # 同波 gather 未完成的
                if wave_tasks:
                    # Day 12: HYBRID resume gather 前 abort check + 外层 try/except
                    if is_aborted(session_id):
                        self._handle_abort(session_id, events)
                        return events, session_id
                    try:
                        results = await asyncio.gather(*wave_tasks, return_exceptions=True)
                    except asyncio.CancelledError:
                        self._handle_abort(session_id, events)
                        raise
                    for node_idx, result in zip(layer, results):
                        agent = agents[node_idx]
                        step_id = f"{session_id}::step{node_idx}"
                        if isinstance(result, Exception):
                            _update_run_status(session_id, node_idx, "failed")
                            events.append(SchedulerEvent(
                                "agent_failed", agent.name, node_idx, wave_idx,
                                error=str(result), run_id=step_id,
                                session_id=session_id,
                            ))
                            print(f"  ❌ [{node_idx}] {agent.name} → {result}")
                        else:
                            self.checkpointer.save_step_committed(session_id, step_id, result)
                            _update_run_status(session_id, node_idx, "completed", result)
                            events.append(SchedulerEvent(
                                "agent_completed", agent.name, node_idx, wave_idx,
                                output=result, run_id=step_id,
                                session_id=session_id,
                            ))
                            context[agent.name] = result
                            print(f"  ✅ [{node_idx}] {agent.name} ({len(result)} chars)")

                # 把已跳过的 completed 事件也发出去
                events.extend([e for e in wave_events if e.event == "agent_completed"])

        elif mode == "SEQUENTIAL":
            # ── SEQUENTIAL：按顺序跑，已 COMMITTED 的 output 传给下一个 ──
            prev_output = original_prompt
            for i, agent in enumerate(agents):
                step_id = f"{session_id}::step{i}"
                if i in committed_idx:
                    output = self.checkpointer.get_committed_output(session_id, step_id)
                    print(f"  ⏭ step {i} {agent.name} → 已 COMMITTED → 跳过")
                    events.append(SchedulerEvent(
                        "agent_completed", agent.name, i, wave=i,
                        output=output or "(empty)",
                        run_id=step_id, session_id=session_id,
                    ))
                    prev_output = output or prev_output
                else:
                    print(f"  🔄 step {i} {agent.name} → 重跑")
                    run_db_id = run_id_by_idx.get(i, 0)
                    events.append(SchedulerEvent(
                        "agent_started", agent.name, i, wave=i,
                        run_id=step_id, session_id=session_id,
                    ))
                    # Day 12: resume 重跑也要过 HITL
                    hitl_result = await self._check_approval(
                        agent, session_id, step_id, run_db_id, prev_output,
                        events, step_index=i, wave=i,
                    )
                    if hitl_result != "approved":
                        _update_run_status(session_id, i, "skipped")
                        events.append(SchedulerEvent(
                            "agent_skipped", agent.name, i, wave=i,
                            run_id=step_id, session_id=session_id,
                            error=f"resume HITL {hitl_result}",
                        ))
                        print(f"  ⏭ resume HITL {hitl_result}: 跳过 {agent.name}")
                        # SEQUENTIAL 被拒绝 → 后续 step 继续
                    else:
                        _update_run_status(session_id, i, "running")
                        try:
                            output = await self._with_retry(
                                lambda agent=agent, prev=prev_output: call_llm_async(
                                    model=DEFAULT_MODEL,
                                    system_prompt=agent.system_prompt,
                                    user_prompt=f"上一步输出（截断前 200 字）: {prev[:200]}\n\n原始 prompt: {original_prompt}",
                                ),
                                step_index=i, session_id=session_id,
                            )
                        except asyncio.CancelledError:
                            # Day 12: resume SEQUENTIAL abort 收尾
                            _update_run_status(session_id, i, "aborted")
                            self._handle_abort(session_id, events)
                            raise
                        self.checkpointer.save_step_committed(session_id, step_id, output)
                        _update_run_status(session_id, i, "completed", output)
                        events.append(SchedulerEvent(
                            "agent_completed", agent.name, i, wave=i,
                            output=output, run_id=step_id, session_id=session_id,
                        ))
                        prev_output = output

        elif mode == "PARALLEL":
            # ── PARALLEL：未 COMMITTED 的先过 HITL，再 gather 并行 ──
            tasks: list = []
            pending: list[tuple[int, AgentDefinition, str]] = []
            for i, agent in enumerate(agents):
                step_id = f"{session_id}::step{i}"
                run_db_id = run_id_by_idx.get(i, 0)
                if i in committed_idx:
                    output = self.checkpointer.get_committed_output(session_id, step_id)
                    print(f"  ⏭ step {i} {agent.name} → 已 COMMITTED → 跳过")
                    events.append(SchedulerEvent(
                        "agent_completed", agent.name, i, wave=0,
                        output=output or "(empty)",
                        run_id=step_id, session_id=session_id,
                    ))
                else:
                    print(f"  🔄 step {i} {agent.name} → 重跑（先过 HITL）")
                    events.append(SchedulerEvent(
                        "agent_started", agent.name, i, wave=0,
                        run_id=step_id, session_id=session_id,
                    ))
                    # Day 12: resume HITL（每个未 COMMITTED 的都要过）
                    hitl_result = await self._check_approval(
                        agent, session_id, step_id, run_db_id, original_prompt,
                        events, step_index=i, wave=0,
                    )
                    if hitl_result != "approved":
                        _update_run_status(session_id, i, "skipped")
                        events.append(SchedulerEvent(
                            "agent_skipped", agent.name, i, wave=0,
                            run_id=step_id, session_id=session_id,
                            error=f"resume HITL {hitl_result}",
                        ))
                        print(f"  ⏭ resume HITL {hitl_result}: 跳过 {agent.name}")
                    else:
                        _update_run_status(session_id, i, "running")
                        tasks.append(self._with_retry(
                            lambda agent=agent: call_llm_async(
                                model=DEFAULT_MODEL,
                                system_prompt=agent.system_prompt,
                                user_prompt=original_prompt,
                            ),
                            step_index=i, session_id=session_id,
                        ))
                        pending.append((i, agent, step_id))

            if tasks:
                # Day 12: resume gather 前 abort check + 外层 try/except
                if is_aborted(session_id):
                    self._handle_abort(session_id, events)
                    return events, session_id
                try:
                    results = await asyncio.gather(*tasks, return_exceptions=True)
                except asyncio.CancelledError:
                    self._handle_abort(session_id, events)
                    raise
                for (node_idx, agent, step_id), result in zip(pending, results):
                    if isinstance(result, Exception):
                        _update_run_status(session_id, node_idx, "failed")
                        events.append(SchedulerEvent(
                            "agent_failed", agent.name, node_idx, wave=0,
                            error=str(result), run_id=step_id,
                            session_id=session_id,
                        ))
                    else:
                        self.checkpointer.save_step_committed(session_id, step_id, result)
                        _update_run_status(session_id, node_idx, "completed", result)
                        events.append(SchedulerEvent(
                            "agent_completed", agent.name, node_idx, wave=0,
                            output=result, run_id=step_id, session_id=session_id,
                        ))

        else:  # DIRECT
            # ── DIRECT：单 step ──
            if 0 in committed_idx:
                output = self.checkpointer.get_committed_output(session_id, f"{session_id}::step0")
                print(f"  ⏭ step 0 → 已 COMMITTED → 跳过")
                events.append(SchedulerEvent(
                    "agent_completed", agents[0].name, 0, wave=0,
                    output=output or "(empty)",
                    run_id=f"{session_id}::step0", session_id=session_id,
                ))
            else:
                print(f"  🔄 step 0 {agents[0].name} → 重跑")
                step_id = f"{session_id}::step0"
                run_db_id = run_id_by_idx.get(0, 0)
                events.append(SchedulerEvent(
                    "agent_started", agents[0].name, 0, wave=0,
                    run_id=step_id, session_id=session_id,
                ))
                # Day 12: resume 重跑也要过 HITL（STARTED 状态 = 审批可能已过期）
                hitl_result = await self._check_approval(
                    agents[0], session_id, step_id, run_db_id, original_prompt,
                    events, step_index=0, wave=0,
                )
                if hitl_result != "approved":
                    _update_run_status(session_id, 0, "skipped")
                    events.append(SchedulerEvent(
                        "agent_skipped", agents[0].name, 0, wave=0,
                        run_id=step_id, session_id=session_id,
                        error=f"resume HITL {hitl_result}",
                    ))
                    print(f"  ⏭ resume HITL {hitl_result}: 跳过")
                else:
                    _update_run_status(session_id, 0, "running")
                    try:
                        output = await self._with_retry(
                            lambda: call_llm_async(
                                model=DEFAULT_MODEL,
                                system_prompt=agents[0].system_prompt,
                                user_prompt=original_prompt,
                            ),
                            step_index=0, session_id=session_id,
                        )
                    except asyncio.CancelledError:
                        # Day 12: resume DIRECT abort 收尾
                        _update_run_status(session_id, 0, "aborted")
                        self._handle_abort(session_id, events)
                        raise
                    self.checkpointer.save_step_committed(session_id, step_id, output)
                    _update_run_status(session_id, 0, "completed", output)
                    events.append(SchedulerEvent(
                        "agent_completed", agents[0].name, 0, wave=0,
                        output=output, run_id=step_id, session_id=session_id,
                    ))

        # ── 6. session 收尾 ──
        self._close_session(session_id, "completed")
        print(f"  ✅ 恢复完成！总共 {len(events)} 个事件")
        return events, session_id

    def _auto_fix_run_status(self, session_id: str, committed_idx: set[int]) -> None:
        """Day 8: agent_runs.status='running' 但 checkpoint 全 COMMITTED → 自动修正为 completed"""
        if not committed_idx:
            return
        sorted_idx = sorted(committed_idx)
        idx_ph = ",".join(f":idx_{i}" for i in range(len(sorted_idx)))
        idx_params = {f"idx_{i}": v for i, v in enumerate(sorted_idx)}
        with engine.begin() as conn:
            conn.execute(text(f"""
                UPDATE agent_runs SET status = 'completed', completed_at = :ts
                WHERE session_id = :sid
                  AND step_index IN ({idx_ph})
                  AND status = 'running'
            """), {"ts": _now_iso(), "sid": session_id, **idx_params})

    def _handle_abort(self, session_id: str, events: list[SchedulerEvent]) -> None:
        """Day 11: abort 时统一收尾 —— agent_runs 未完成的标 aborted + sessions 标 aborted"""
        with engine.begin() as conn:
            conn.execute(text("""
                UPDATE agent_runs SET status = 'aborted', completed_at = :ts
                WHERE session_id = :sid AND status NOT IN ('completed', 'failed', 'skipped')
            """), {"ts": _now_iso(), "sid": session_id})
            conn.execute(text("""
                UPDATE sessions SET status = 'aborted', completed_at = :ts
                WHERE session_id = :sid AND status NOT IN ('completed', 'failed')
            """), {"ts": _now_iso(), "sid": session_id})
        events.append(SchedulerEvent(
            "aborted", agent_name="(session)", step_index=-1, wave=-1,
            session_id=session_id, error="aborted_by_user",
        ))
        print(f"  🛑 session {session_id} abort 收尾完成（未完成 agent_runs → aborted）")

    # ── 收尾 ──

    def _close_session(self, session_id: str, status: str) -> None:
        with engine.begin() as conn:
            conn.execute(text("""
                UPDATE sessions SET status = :s, output_json = '{}', completed_at = :t
                WHERE session_id = :sid
            """), {"s": status, "t": _now_iso(), "sid": session_id})


# ============================================================
# 启动入口（Day 4 一次性验证三种模式）
# ============================================================

async def main():
    engine = SchedulerEngine()

    # ── DIRECT ──
    print("\n" + "=" * 60)
    print("TEST 1: DIRECT (1 agent) + checkpoint")
    print("=" * 60)
    events, _ = await engine.run("DIRECT", ["drize-engineer"], "测试推演引擎")
    assert events[-1].event == "agent_completed", "DIRECT 应该 completed"
    assert events[0].step_index == 0, "DIRECT step_index=0"

    # ── SEQUENTIAL ──
    print("\n" + "=" * 60)
    print("TEST 2: SEQUENTIAL (3 agents) + checkpoint")
    print("=" * 60)
    events, _ = await engine.run(
        "SEQUENTIAL",
        ["drize-engineer", "arch-designer", "project-taker"],
        "链式测试",
    )
    completed = [e for e in events if e.event == "agent_completed"]
    assert len(completed) == 3, f"SEQ 应该 3 个 completed，实际 {len(completed)}"
    assert [e.step_index for e in completed] == [0, 1, 2], "SEQ step_index 应该 0→1→2"

    # ── PARALLEL ──
    print("\n" + "=" * 60)
    print("TEST 3: PARALLEL (3 agents) + checkpoint")
    print("=" * 60)
    events, _ = await engine.run(
        "PARALLEL",
        ["rca-analyst", "backend-expert", "safety-auditor"],
        "并行测试",
    )
    completed = [e for e in events if e.event == "agent_completed"]
    assert len(completed) == 3, f"PAR 应该 3 个 completed，实际 {len(completed)}"
    assert all(e.wave == 0 for e in completed), "PARALLEL wave 应该全 0"

    # ── CHECKPOINT 验证 ──
    print("\n" + "=" * 60)
    print("TEST 4: checkpoint WAL 验证")
    print("=" * 60)
    from sqlalchemy import text as sa_text

    # 直接查 checkpoints 表
    from db import engine as db_engine
    with db_engine.connect() as conn:
        rows = conn.execute(sa_text("""
            SELECT session_id, step_index, status, committed_at IS NOT NULL
            FROM checkpoints ORDER BY session_id, step_index
        """)).fetchall()
        print(f"checkpoints 总数: {len(rows)}")
        committed_count = sum(1 for r in rows if r[3])
        print(f"  COMMITTED: {committed_count} / {len(rows)}")
        assert committed_count == len(rows), "所有 step 应该都 COMMITTED"
        print("  ✅ checkpoint WAL 写入正确！")

    # ── RESUME 验证 ──
    print("\n" + "=" * 60)
    print("TEST 5: resume_from（从 checkpoint 恢复）")
    print("=" * 60)
    with db_engine.connect() as conn:
        last_session = conn.execute(sa_text("""
            SELECT session_id FROM sessions ORDER BY rowid DESC LIMIT 1
        """)).fetchone()[0]
    print(f"恢复 session: {last_session}")
    resume_events, _sid = await engine.resume_from(last_session)
    # 应该全是 COMMITTED → 跳过，不会重跑
    assert all(e.event == "agent_completed" for e in resume_events), "恢复时所有 step 应该已 COMMITTED"
    print("  ✅ resume_from 跳过已 COMMITTED 步骤！")

    # ── HYBRID DAG 测试 ──
    print("\n" + "=" * 60)
    print("TEST 6: HYBRID DAG + TopologicalSorter 分层")
    print("=" * 60)
    # 手动指定 layers 避免 depends_on 为空导致全在 Layer 0
    # 5 个 agent → Layer 0 两个并行 → Layer 1 一个 → Layer 2 两个并行
    agents_5 = ["drize-engineer", "arch-designer", "project-taker", "rca-analyst", "backend-expert"]
    manual_layers = [[0, 1], [2], [3, 4]]  # Layer 0 并行 → Layer 1 等 → Layer 2 并行
    print(f"  手动 layers: {manual_layers}")
    events, _ = await engine.run("HYBRID", agents_5, "DAG 测试", layers=manual_layers)

    # wave 验证
    started = [e for e in events if e.event == "agent_started"]
    completed = [e for e in events if e.event == "agent_completed"]
    waves_seen = sorted({e.wave for e in started})
    print(f"  启动 waves: {waves_seen}（应该 0,1,2）")
    assert waves_seen == [0, 1, 2], f"HYBRID 应该有 3 层 wave，实际 {waves_seen}"
    assert len(completed) == 5, f"HYBRID 应该 5 个 completed"
    print(f"  ✅ HYBRID DAG 分层正确！5 agents × 3 waves")

    # 验证 build_layers 自动（depends_on 非空 → 自动分层）
    auto_layers = engine.build_layers(engine.registry.load_all()[:5])
    print(f"  build_layers 自动（depends_on 非空）: {auto_layers}")
    # 应该至少 1 层，最多 5 层（每个 agent 独立一层）
    assert 1 <= len(auto_layers) <= 5, f"layers 数应该 1-5，实际 {len(auto_layers)}"
    # 所有 agent index 应该都覆盖
    all_nodes = sorted(n for layer in auto_layers for n in layer)
    assert all_nodes == [0, 1, 2, 3, 4], f"应该覆盖所有 5 个 agent"
    print(f"  ✅ build_layers 自动分层正确！")

    # ── 汇总 ──
    print("\n" + "=" * 60)
    print("✅ Day 7 HYBRID DAG + TopologicalSorter 全部通过！")
    print("   四种调度模式 (DIRECT/SEQ/PARALLEL/HYBRID) 全部实现！")
    print("=" * 60)


if __name__ == "__main__":
    asyncio.run(main())
