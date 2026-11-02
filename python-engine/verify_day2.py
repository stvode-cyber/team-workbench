"""Day 2 验收脚本：FastAPI 入口 4 个端点全联调

前提：Day 1 已过（DB schema + PRAGMA + 索引）
本脚本验收：
  ① GET  /health            探活（db_ok + litellm_ok）
  ② GET  /api/agents        AgentRegistry 拉 11 个专岗
  ③ POST /api/chat/stream   SSE 主入口 DIRECT 模式 + mock LiteLLM
  ④ POST /api/chat/stream   SSE 主入口 HYBRID 模式（含 DAG wave 分层）

启动：
  python -c "import uvicorn; uvicorn.run('main:app', host='127.0.0.1', port=8765)"
  然后另跑：python verify_day2.py
"""

from __future__ import annotations

import json
import sys
import time
import urllib.request
import urllib.error

BASE = "http://127.0.0.1:8765"
PASS = 0
FAIL = 0


def check(label: str, ok: bool, detail: str = ""):
    global PASS, FAIL
    if ok:
        PASS += 1
        print(f"  ✅ {label}")
    else:
        FAIL += 1
        print(f"  ❌ {label}  {detail}")


def http_get(path: str) -> tuple[int, dict | str]:
    try:
        with urllib.request.urlopen(f"{BASE}{path}", timeout=10) as resp:
            body = resp.read().decode("utf-8")
            if resp.headers.get_content_type().startswith("application/json"):
                return resp.status, json.loads(body)
            return resp.status, body
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8")
    except Exception as e:
        return 0, str(e)


def http_post(path: str, payload: dict) -> tuple[int, dict | str, str]:
    """返回 (status, body_json_or_str, content_type)，headers 里只关心 Content-Type"""
    try:
        data = json.dumps(payload).encode("utf-8")
        req = urllib.request.Request(
            f"{BASE}{path}", data=data,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=60) as resp:
            body = resp.read().decode("utf-8")
            ct = resp.headers.get("Content-Type") or resp.headers.get("content-type") or ""
            if resp.headers.get_content_type().startswith("application/json"):
                return resp.status, json.loads(body), ct
            return resp.status, body, ct
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8"), {}
    except Exception as e:
        return 0, str(e), {}


# ============================================================
# 前置：服务器能连上吗？
# ============================================================
print("=== 前置：服务器连通性 ===")
status, _ = http_get("/health")
if status == 0:
    print(f"❌ 服务器没起来（{BASE}），先启动 uvicorn")
    sys.exit(1)
print(f"  ✅ 服务器在线（HTTP {status}）")

# ============================================================
# ① GET /health
# ============================================================
print("\n=== ① GET /health ===")
status, body = http_get("/health")
check("HTTP 200", status == 200, f"got {status}")
if isinstance(body, dict):
    check("status == 'ok'", body.get("status") == "ok", str(body))
    check("db_ok == True", body.get("db_ok") is True)
    check("litellm_ok == True", body.get("litellm_ok") is True)
    check("version", bool(body.get("version")))
else:
    check("返回 JSON", False, f"got {body}")

# ============================================================
# ② GET /api/agents
# ============================================================
print("\n=== ② GET /api/agents ===")
status, body = http_get("/api/agents")
check("HTTP 200", status == 200, f"got {status}")
if isinstance(body, list):
    check("agent 总数 >= 11", len(body) >= 11, f"got {len(body)}")
    names = [a.get("name") for a in body]
    for must in ("drize-engineer", "arch-designer", "mcp-finder"):
        check(f"有 {must}", must in names)
    # 全 installed
    all_installed = all(a.get("install_status") == "installed" for a in body)
    check("全部 install_status=installed", all_installed)
else:
    check("返回 list[dict]", False, str(body))

# ============================================================
# ③ POST /api/chat/stream — DIRECT 模式
# ============================================================
print("\n=== ③ POST /api/chat/stream · DIRECT 模式 ===")
payload = {
    "mode": "DIRECT",
    "agent_names": ["drize-engineer"],
    "prompt": "verify_day2.py DIRECT 测试",
}
status, body, ct = http_post("/api/chat/stream", payload)
check("HTTP 200", status == 200, f"got {status}")
check("Content-Type: text/event-stream", "event-stream" in ct, ct)

# 解析 SSE 事件
if isinstance(body, str):
    events = []
    current_event = None
    for line in body.split("\n"):
        if line.startswith("event:"):
            current_event = line[6:].strip()
        elif line.startswith("data:") and current_event:
            events.append(current_event)
            current_event = None
    # 检查事件链
    check("run_started", "run_started" in events, events)
    check("agent_started", "agent_started" in events, events)
    check("agent_completed", "agent_completed" in events, events)
    check("session_complete", "session_complete" in events, events)

# ============================================================
# ④ POST /api/chat/stream — HYBRID 模式（多 agent DAG）
# ============================================================
print("\n=== ④ POST /api/chat/stream · HYBRID 模式 ===")
payload_hybrid = {
    "mode": "HYBRID",
    "agent_names": ["drize-engineer", "rca-analyst"],  # 2 个 agent 应有 wave 分层
    "prompt": "verify_day2.py HYBRID DAG 测试",
}
status, body, _ct = http_post("/api/chat/stream", payload_hybrid)
check("HTTP 200", status == 200, f"got {status}")

if isinstance(body, str):
    events = []
    for line in body.split("\n"):
        if line.startswith("event:"):
            events.append(line[6:].strip())
    check("run_started", "run_started" in events)
    check("agent_started", "agent_started" in events)
    check("agent_completed", "agent_completed" in events)
    check("session_complete", "session_complete" in events)

# ============================================================
# ⑤ 400 错误：agent 名字不对
# ============================================================
print("\n=== ⑤ 400 错误路径 ===")
payload_bad = {
    "mode": "DIRECT",
    "agent_names": ["nonexistent-agent"],
    "prompt": "应该 400",
}
status, body, _ = http_post("/api/chat/stream", payload_bad)
check("HTTP 400（坏 agent 名）", status == 400, f"got {status}")

# ============================================================
# 总结
# ============================================================
print(f"\n═══ 总结 ═══")
print(f"  通过 {PASS} 个 / 失败 {FAIL} 个")
if FAIL == 0:
    print("  🎉 Day 2 FastAPI 入口验收全部通过！")
else:
    print(f"  ❌ 有 {FAIL} 个失败项需要跟进")
sys.exit(0 if FAIL == 0 else 1)
