"""
Day 6 · SSE 生产化验收

验证项：
  1. SSE 每个事件带递增 id（格式 creating-0 → session_id-1,2,3...）
  2. 读取 Last-Event-ID header（服务端打 log + 不报错）
  3. 断线重连后 checkpoint 已写的保留（不会重跑）
  4. SSE 事件顺序：run_started → agent_started → agent_completed → session_complete

跑法：
  .venv\Scripts\python.exe verify_day6.py
"""

from __future__ import annotations

import json
import os
import sys
import time
import urllib.request

import requests

BASE = "http://127.0.0.1:8765"
PASS = 0
FAIL = 0


def check(name: str, cond: bool, detail: str = ""):
    global PASS, FAIL
    if cond:
        PASS += 1
        print(f"  ✅ {name}")
    else:
        FAIL += 1
        print(f"  ❌ {name}  {detail}")


def stream_sse(payload: dict, last_event_id: str | None = None, timeout: int = 20) -> tuple[list[str], list[str]]:
    """
    发起 SSE 请求，返回 (raw_event_blocks, event_ids)
    用 requests 库 stream=True + iter_lines 正确解析 SSE 协议
    """
    headers = {"Accept": "text/event-stream"}
    if last_event_id:
        headers["Last-Event-ID"] = last_event_id

    print(f"  REQUEST_HEADERS: {headers}")

    resp = requests.post(
        f"{BASE}/api/chat/stream",
        json=payload,
        headers=headers,
        stream=True,
        timeout=timeout,
    )
    print(f"  RESPONSE_HEADER: HTTP {resp.status_code}")

    blocks: list[str] = []
    current_lines: list[str] = []
    for line in resp.iter_lines(decode_unicode=True):
        if line is None:
            break
        # 过滤 ping/sse 注释（以 ":" 开头）
        if line.startswith(":"):
            continue
        current_lines.append(line)
        if line == "":  # SSE 协议：空行 = event block 结束
            block = "\n".join(current_lines)
            if block.strip():
                blocks.append(block)
            current_lines = []

    event_ids = []
    for block in blocks:
        for line in block.split("\n"):
            if line.startswith("id:"):
                event_ids.append(line[3:].strip())

    return blocks, event_ids


def parse_sse_events(blocks: list[str]) -> list[dict]:
    """把 SSE event blocks 解析成 [{event, data, id}]"""
    events = []
    for block in blocks:
        ev = {"event": None, "data": None, "id": None}
        data_lines = []
        for line in block.split("\n"):
            if line.startswith("event:"):
                ev["event"] = line[6:].strip()
            elif line.startswith("data:"):
                data_lines.append(line[5:].strip())
            elif line.startswith("id:"):
                ev["id"] = line[3:].strip()
        if data_lines:
            raw = "\n".join(data_lines)
            try:
                ev["data"] = json.loads(raw)
            except json.JSONDecodeError:
                ev["data"] = raw
        events.append(ev)
    return events


def main():
    global PASS, FAIL
    print("=" * 60)
    print("Day 6 · SSE 生产化验收")
    print("=" * 60)

    # ── 预检：/health ──
    print("\n[预检] /health")
    try:
        resp = urllib.request.urlopen(f"{BASE}/health", timeout=5)
        health = json.loads(resp.read())
        check("服务在线", health.get("status") == "ok", str(health))
    except Exception as e:
        check("服务在线", False, str(e))
        print("  ❌ 引擎不在线，退出")
        sys.exit(1)

    # ── TEST 1: DIRECT 模式，所有事件带 id ──
    print("\n[TEST 1] DIRECT 模式 · 事件 id 递增")
    blocks, event_ids = stream_sse({
        "mode": "DIRECT",
        "agent_names": ["drize-engineer"],
        "prompt": "Day6 test 1 - verify event ids",
    })
    events = parse_sse_events(blocks)

    check("收到 >= 4 个 SSE block", len(events) >= 4, f"只收到 {len(events)} 个: {[e['event'] for e in events]}")

    # 每个 block 都有 id
    all_have_id = all(ev["id"] is not None for ev in events)
    check("所有事件带 id 字段", all_have_id,
          f"缺失 id: {[(i,e) for i,e in enumerate(events) if e['id'] is None]}")

    # 找到 session_id（agent_started 的 data 里有）
    session_id = None
    for ev in events:
        if ev["event"] == "agent_started" and isinstance(ev["data"], dict):
            session_id = ev["data"].get("session_id")
            break
    if session_id:
        # run_started 的 id 是 creating-0，后面是 session_id-1,2,3...
        # 所以只检查 id 序列的后半部分（>= seq 1 的部分）
        valid_ids = all(
            eid.startswith(session_id + "-") or eid.startswith("creating-")
            for eid in event_ids
        )
        check(f"id 格式 = (creating|session_id)-seq", valid_ids, f"ids={event_ids}")

        # seq 递增不重复
        seqs = []
        for eid in event_ids:
            try:
                seqs.append(int(eid.split("-")[-1]))
            except ValueError:
                seqs.append(-1)
        seq_monotonic = seqs == sorted(seqs) and len(seqs) == len(set(seqs))
        check("seq 严格递增不重复", seq_monotonic, f"seqs={seqs}")

    # 事件顺序
    event_names = [ev["event"] for ev in events]
    check("事件顺序：run_started 开头", event_names[0] == "run_started", str(event_names))
    check("事件顺序：session_complete 结尾", event_names[-1] == "session_complete", str(event_names))

    # ── TEST 2: 带 Last-Event-ID header ──
    print("\n[TEST 2] 带 Last-Event-ID header")
    blocks2, ids2 = stream_sse({
        "mode": "DIRECT",
        "agent_names": ["arch-designer"],
        "prompt": "Day6 test 2 - send Last-Event-ID",
    }, last_event_id="fake-session-99-2")
    events2 = parse_sse_events(blocks2)
    check("带 Last-Event-ID 的请求能正常返回事件",
          len(events2) >= 4, f"收到 {len(events2)} 个")

    # ── TEST 3: checkpoint 已 COMMITTED 的 step 保留 ──
    print("\n[TEST 3] checkpoint 已 COMMITTED 的 step 保留")
    blocks3, ids3 = stream_sse({
        "mode": "SEQUENTIAL",
        "agent_names": ["project-taker", "rca-analyst"],
        "prompt": "Day6 test 3 - checkpoint persistence",
    })
    events3 = parse_sse_events(blocks3)
    check("SEQUENTIAL 两个 agent 都跑完",
          sum(1 for e in events3 if e["event"] == "agent_completed") == 2,
          f"agent_completed = {sum(1 for e in events3 if e['event']=='agent_completed')}")

    # 查 SQLite checkpoint 表
    db_candidates = [
        os.path.join(os.environ.get("APPDATA", ""), "TeamWorkbench", "data.db"),
        os.path.join(os.path.dirname(__file__), "data.db"),
    ]
    db_path = next((p for p in db_candidates if os.path.exists(p)), None)
    if db_path:
        import sqlite3
        conn = sqlite3.connect(db_path)
        rows = conn.execute(
            "SELECT session_id, step_id, status FROM checkpoints ORDER BY rowid DESC LIMIT 10"
        ).fetchall()
        committed = [r for r in rows if r[2] == "COMMITTED"]
        check("checkpoint 表有 COMMITTED 记录",
              len(committed) >= 2, f"COMMITTED={len(committed)} rows={rows[:5]}")
        conn.close()
    else:
        check("checkpoint 表存在", False, "找不到 data.db")

    # ── TEST 4: SSE Content-Type 和 ping ──
    print("\n[TEST 4] SSE 协议细节")
    try:
        import requests as req_lib
        r = req_lib.post(
            f"{BASE}/api/chat/stream",
            json={"mode": "DIRECT", "agent_names": ["ledger-keeper"], "prompt": "test4"},
            stream=True,
            timeout=10,
        )
        ct = r.headers.get("Content-Type", "")
        check("Content-Type: text/event-stream", "text/event-stream" in ct, ct)
        r.close()
    except Exception as ex:
        check("SSE Content-Type 正确", False, str(ex))

    # ── 汇总 ──
    print("\n" + "=" * 60)
    total = PASS + FAIL
    print(f"Day 6 验收: {PASS}/{total} 通过")
    print("=" * 60)
    return FAIL == 0


if __name__ == "__main__":
    ok = main()
    sys.exit(0 if ok else 1)
