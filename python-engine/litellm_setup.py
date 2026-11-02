"""
LiteLLM 初始化 + OpenRouter 主 + Ollama fallback + Mock 模式

设计（精确对齐指南针 ⑥ L305-329 + D3 决策）：
  - OpenRouter（主路由）：需要 OPENROUTER_API_KEY 环境变量
  - Ollama（fallback）：localhost:11434，本地模型
  - Mock 模式：没 OpenRouter key + Ollama 没装时自动启用（Day 3 先用这个）
  - 每个 agent 用哪个 LLM → 查 SQLite llm_config 表

Mock 检测逻辑：
  1) OPENROUTER_API_KEY 没设 → mock=True
  2) Ollama 端口 11434 没响应 → mock=True
  3) 两个都有 → 正常模式
"""

from __future__ import annotations

import json
import os
from datetime import datetime
from typing import Optional

import litellm
from litellm import completion, acompletion


# ============================================================
# 环境检测（启动时跑一次）
# ============================================================

OPENROUTER_API_KEY = os.environ.get("OPENROUTER_API_KEY", "").strip()


def _ollama_available() -> bool:
    """探测 Ollama localhost:11434（用标准库 urllib，不装新包）"""
    import urllib.request
    import urllib.error
    try:
        req = urllib.request.Request(
            "http://localhost:11434/api/tags",
            method="GET",
        )
        with urllib.request.urlopen(req, timeout=1) as resp:
            return resp.status == 200
    except (urllib.error.URLError, OSError, TimeoutError):
        return False


def _detect_mode() -> str:
    """返回 'real' 或 'mock'"""
    ollama_ok = _ollama_available()
    openrouter_ok = bool(OPENROUTER_API_KEY)

    if openrouter_ok or ollama_ok:
        return "real"
    return "mock"


# ============================================================
# 初始化
# ============================================================

MODE = _detect_mode()
print(f"[LiteLLM] 检测完成 → 模式: {MODE}")
print(f"  OPENROUTER_API_KEY: {'✅ 已设' if OPENROUTER_API_KEY else '❌ 未设'}")
print(f"  Ollama :11434:     {'✅ 在线' if _ollama_available() else '❌ 离线'}")


def setup_litellm() -> None:
    """全局配置（精确对齐指南针 ⑥ L313-325 + Day 9 D8 retry 生产化）"""
    if MODE != "real":
        print("[LiteLLM] Mock 模式 → 跳过真实配置")
        return

    # OpenRouter（主路由）
    litellm.api_base = "https://openrouter.ai/api/v1"

    # 全局 fallback（指南针 D3：openrouter 超时 → ollama）
    litellm.fallback_list = [
        {"model": "openrouter/gpt-4o-mini"},
        {"model": "ollama/qwen2.5:7b"},
    ]

    # Day 9: 两层 retry 分离配置
    # LiteLLM 只管「单次调用 + provider 超时 + fallback 切换」
    # retry/指数退避/abort 检查全由 scheduler._with_retry 外层控制
    # —— 否则两层叠加 = 9 次调用 + 每次等 6000s 超时（严重 bug）
    litellm.request_timeout = 60   # 单 provider 60s 超时
    litellm.retry = False          # 关 LiteLLM 内部 retry

    # 单例配置
    litellm.drop_params = True
    litellm.set_verbose = False
    print("[LiteLLM] 真实模式配置完成 ✅")
    print(f"  request_timeout=60s | retry=False | fallback_list={'ON' if litellm.fallback_list else 'OFF'}")


setup_litellm()


# ============================================================
# Day 9: Mock 失败注入（verify_day9.py 用）
# ============================================================

# 模块级变量：Mock 模式下前 N 次调用抛 Exception，
# 用于测试 scheduler._with_retry 的指数退避全链路
_mock_fail_remaining: int = 0


def configure_mock_fail(remaining: int) -> None:
    """verify_day9.py 调用：设置后续 Mock 调用前 remaining 次会失败"""
    global _mock_fail_remaining
    _mock_fail_remaining = remaining
    print(f"  🎯 [Mock Fail] 已配置：接下来 {remaining} 次调用会抛异常")


def _maybe_mock_fail() -> None:
    """内部函数：检查是否该抛异常"""
    global _mock_fail_remaining
    if _mock_fail_remaining > 0:
        _mock_fail_remaining -= 1
        raise RuntimeError(
            f"[MOCK SIMULATED FAILURE] remaining_failures_before_this_call={_mock_fail_remaining}"
        )


# ============================================================
# Mock 响应（模拟真实 LLM 返回）
# ============================================================

def _mock_completion_response(model: str, prompt: str) -> litellm.ModelResponse:
    """造一个长得像真实 LLM 返回的 Mock response"""
    content = (
        f"[MOCK] model={model} | 输入长度={len(prompt)} 字符\n"
        f"agent 已执行。这是 Day 3 Mock 模式返回。\n"
        f"时间戳: {datetime.utcnow().isoformat()}"
    )

    # 用 litellm 的 ModelResponse 结构
    mock_msg = litellm.Choices(
        message=litellm.Message(content=content, role="assistant"),
        index=0,
        finish_reason="stop",
    )
    mock_resp = litellm.ModelResponse(
        id=f"mock-{datetime.utcnow().timestamp()}",
        model=model,
        choices=[mock_msg],
        usage=litellm.Usage(prompt_tokens=len(prompt) // 4, completion_tokens=len(content) // 4, total_tokens=len(prompt) // 4 + len(content) // 4),
    )
    return mock_resp


# ============================================================
# 统一调用入口
# ============================================================

async def call_llm_async(
    model: str,
    system_prompt: str,
    user_prompt: str,
    temperature: float = 0.7,
    max_tokens: int = 4096,
    **kwargs,
) -> str:
    """异步调用 LLM（Day 4 Scheduler DIRECT/SEQ/PARALLEL 用）"""
    if MODE == "mock":
        import asyncio
        await asyncio.sleep(0.5)  # Day 15: mock 延迟 0.5s（给 abort 冒烟 window）
        # Day 9: Mock 失败注入（verify_day9.py 测 retry 全链路）
        _maybe_mock_fail()
        return _mock_completion_response(model, system_prompt + user_prompt).choices[0].message.content

    messages = [
        {"role": "system", "content": system_prompt},
        {"role": "user", "content": user_prompt},
    ]
    resp = await acompletion(
        model=model,
        messages=messages,
        temperature=temperature,
        max_tokens=max_tokens,
        **kwargs,
    )
    return resp.choices[0].message.content


def call_llm(
    model: str,
    system_prompt: str,
    user_prompt: str,
    temperature: float = 0.7,
    max_tokens: int = 4096,
    **kwargs,
) -> str:
    """同步调用 LLM（简单场景用）"""
    if MODE == "mock":
        # Day 9: Mock 失败注入
        _maybe_mock_fail()
        return _mock_completion_response(model, system_prompt + user_prompt).choices[0].message.content

    messages = [
        {"role": "system", "content": system_prompt},
        {"role": "user", "content": user_prompt},
    ]
    resp = completion(
        model=model,
        messages=messages,
        temperature=temperature,
        max_tokens=max_tokens,
        **kwargs,
    )
    return resp.choices[0].message.content


# ============================================================
# 默认模型（没 llm_config 时用）
# ============================================================

DEFAULT_MODEL = "openrouter/gpt-4o-mini"
DEFAULT_PROVIDER = "openrouter"
