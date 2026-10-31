<template>
  <div class="detail" v-if="agent">
    <!-- 头部 -->
    <div class="detail-header">
      <div class="detail-avatar">{{ agent.emoji }}</div>
      <div class="detail-title">
        <h2>{{ agent.cn_name }}</h2>
        <span class="en-tag">{{ agent.name }}</span>
        <span class="pri-tag" :class="agent.priority">
          {{ agent.priority === "P0" ? "🔴 P0 核心岗" : "🟡 P1 支援岗" }}
        </span>
        <div class="desc">{{ agent.desc }}</div>
      </div>
    </div>

    <!-- 🎯 员工说明 -->
    <div class="section">
      <h3>🎯 员工说明</h3>
      <div class="role-block"><p>{{ agent.role }}</p></div>
    </div>

    <!-- 📣 调动指引 -->
    <div class="section">
      <h3>📣 调动指引（什么时候喊我）</h3>
      <div class="trigger-group">
        <div class="trigger-title">主动触发词（直接说）</div>
        <div class="trigger-chips">
          <span v-for="(t, i) in agent.how_to_trigger?.manual" :key="i" class="chip manual">「{{ t }}」</span>
          <span v-if="!agent.how_to_trigger?.manual?.length" class="muted">—</span>
        </div>
      </div>
      <div class="trigger-group">
        <div class="trigger-title">自动触发（AI 识别到就调）</div>
        <div class="trigger-chips">
          <span v-for="(t, i) in agent.how_to_trigger?.auto" :key="i" class="chip auto">🔒 {{ t }}</span>
          <span v-if="!agent.how_to_trigger?.auto?.length" class="muted">—</span>
        </div>
      </div>
    </div>

    <!-- 🔗 调用链 -->
    <div class="section">
      <h3>🔗 调用链（会调谁）</h3>
      <div v-if="agent.depends_on?.length" class="chain">
        <span class="node">{{ agent.cn_name }}</span>
        <span class="arrow">→</span>
        <span v-for="(dep, i) in agent.depends_on" :key="i" class="node">{{ dep }}</span>
      </div>
      <div v-else class="muted" style="font-style: italic;">没有强制调用依赖（可以独立工作）</div>
    </div>

    <!-- 📋 工作流程 -->
    <div class="section" v-if="agent.process?.length">
      <h3>📋 工作流程</h3>
      <ul>
        <li v-for="(p, i) in agent.process" :key="i">{{ p }}</li>
      </ul>
    </div>

    <!-- ⛔ 硬性边界 -->
    <div class="section" v-if="agent.boundary?.length">
      <h3>⛔ 硬性边界</h3>
      <ul>
        <li v-for="(b, i) in agent.boundary" :key="i">{{ b }}</li>
      </ul>
    </div>

    <!-- 🔧 拥有工具 -->
    <div class="section" v-if="agent.tools?.length">
      <h3>🔧 拥有工具</h3>
      <div class="trigger-chips">
        <span v-for="(t, i) in agent.tools" :key="i" class="chip tool">{{ t }}</span>
      </div>
    </div>

    <!-- 📦 本机安装状态 -->
    <div class="section">
      <h3>📦 本机安装状态</h3>
      <div class="role-block">
        <p v-if="agent.install_status === 'installed'">✅ 已完整安装（{{ agent.install_date || "未知日期" }}）</p>
        <p v-else-if="agent.install_status === 'partial'">⚠️ 安装不完整（agent.md 或 SKILL.md 缺失）</p>
        <p v-else>❌ 未安装到本机 Trae 目录</p>
      </div>
    </div>

    <!-- 📡 当前状态 -->
    <div class="section">
      <h3>📡 运行状态</h3>
      <p>
        <strong :class="agent.runtime_status === 'busy' ? 'warn' : 'ok'">
          {{ agent.runtime_status === 'busy' ? '🟡 忙碌' : '🟢 待命' }}
        </strong>
      </p>
      <p v-if="agent.updated_at" class="muted" style="font-size: 11px; margin-top: 4px;">
        最后更新：{{ agent.updated_at }}
      </p>
    </div>

    <!-- 🔧 员工管理（Z-tier） -->
    <div class="section">
      <h3>🔧 员工管理</h3>
      <div class="mgmt-row">
        <button
          class="btn install"
          :disabled="actionLoading"
          @click="runAction('install')"
        >
          {{ actionLoading && actionName === 'install' ? '⏳ 安装中...' : '📦 一键安装' }}
        </button>
        <button
          class="btn status"
          :disabled="actionLoading"
          @click="runAction('toggle-status')"
        >
          {{ agent.runtime_status === 'busy' ? '🟢 待命' : '🟡 标记忙碌' }}
        </button>
        <button class="btn edit" @click="emit('edit', agent)">✏️ 编辑</button>
        <button class="btn uninstall" :disabled="actionLoading" @click="runAction('uninstall')">🗑️ 卸载</button>
        <button class="btn danger" @click="emit('delete', agent)">💀 删除员工</button>
      </div>

      <!-- 执行日志 -->
      <div v-if="log" class="exec-log" :class="log.ok ? 'ok' : 'fail'">
        <div class="log-header" :class="log.ok ? 'log-ok' : 'log-fail'">
          {{ log.ok ? '✅ 成功' : '❌ 失败' }} · {{ actionLabel }}
        </div>
        <pre v-if="log.stdout">{{ log.stdout }}</pre>
        <pre v-if="log.stderr" class="stderr">{{ log.stderr }}</pre>
      </div>
    </div>
  </div>

  <div v-else class="detail empty">
    <div style="text-align: center; padding: 60px 20px; color: var(--muted);">
      <div style="font-size: 48px; margin-bottom: 12px;">👈</div>
      <h2 style="color: #fff;">点左侧查看员工详情</h2>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from "vue";
import type { Agent } from "../api/agents";
import { installAgent, uninstallAgent, setAgentStatus } from "../api/agents";

const props = defineProps<{ agent?: Agent | null }>();
const emit = defineEmits<{
  (e: "edit", a: Agent | null): void;
  (e: "delete", a: Agent | null): void;
}>();

const actionLoading = ref(false);
const actionName = ref("");
const log = ref<{ ok: boolean; stdout: string; stderr: string } | null>(null);

const actionLabel: Record<string, string> = {
  install: "一键安装",
  uninstall: "卸载",
  "toggle-status": "切换运行状态",
};

async function runAction(action: "install" | "uninstall" | "toggle-status") {
  if (!props.agent || actionLoading.value) return;
  actionLoading.value = true;
  actionName.value = action;
  log.value = null;

  try {
    if (action === "install") {
      const r = await installAgent(props.agent.name);
      log.value = { ok: r.ok, stdout: r.stdout || "", stderr: r.stderr || "" };
    } else if (action === "uninstall") {
      const r = await uninstallAgent(props.agent.name);
      log.value = {
        ok: (r.deleted?.length ?? 0) > 0,
        stdout: (r.deleted?.join("\n")) || "No files to delete",
        stderr: (r.errors?.join("\n")) || "",
      };
    } else if (action === "toggle-status") {
      const newStatus = props.agent.runtime_status === "busy" ? "idle" : "busy";
      await setAgentStatus(props.agent.name, newStatus);
      log.value = { ok: true, stdout: `Status → ${newStatus}`, stderr: "" };
    }
  } catch (e: any) {
    log.value = { ok: false, stdout: "", stderr: e?.message || String(e) };
  } finally {
    actionLoading.value = false;
  }
}
</script>

<style scoped>
.detail-header { display: flex; align-items: center; gap: 18px; margin-bottom: 20px; }
.detail-avatar {
  width: 64px; height: 64px; border-radius: 14px;
  display: flex; align-items: center; justify-content: center;
  font-size: 36px; background: rgba(100,200,255,0.1);
}
.detail-title h2 { font-size: 18px; color: #fff; margin-bottom: 4px; }
.en-tag { font-size: 11px; color: var(--accent); background: rgba(100,200,255,0.12); padding: 2px 8px; border-radius: 6px; margin-right: 6px; }
.pri-tag { font-size: 11px; padding: 2px 8px; border-radius: 6px; }
.pri-tag.P0 { background: rgba(239,68,68,0.15); color: var(--red); }
.pri-tag.P1 { background: rgba(251,191,36,0.15); color: var(--yellow); }
.desc { font-size: 11px; color: var(--muted); margin-top: 4px; }

.role-block {
  background: rgba(100,200,255,0.06); border-left: 2px solid var(--accent);
  padding: 10px 14px; border-radius: 0 6px 6px 0; margin-bottom: 8px;
}
.role-block p { font-size: 12px; color: #d0e4f7; line-height: 1.9; }

.trigger-group { margin-bottom: 12px; }
.trigger-group:last-child { margin-bottom: 0; }
.trigger-title { font-size: 11px; color: var(--muted); margin-bottom: 5px; }
.trigger-chips { display: flex; flex-wrap: wrap; gap: 5px; }

.chip { font-size: 11px; padding: 3px 10px; border-radius: 12px; font-family: "Consolas", monospace; }
.chip.manual { background: rgba(96,165,250,0.15); color: #60a5fa; border: 1px solid rgba(96,165,250,0.25); }
.chip.auto { background: rgba(74,222,128,0.12); color: #4ade80; border: 1px solid rgba(74,222,128,0.25); font-family: inherit; }
.chip.tool { background: rgba(168,85,247,0.12); color: #c084fc; border: 1px solid rgba(168,85,247,0.25); font-family: inherit; }

.chain { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.node { background: rgba(100,200,255,0.1); border: 1px solid rgba(100,200,255,0.2); color: var(--accent); font-size: 11px; padding: 3px 9px; border-radius: 10px; }
.arrow { color: var(--accent); font-size: 14px; }

.ok { color: var(--green); }
.warn { color: var(--yellow); }
.muted { color: var(--muted); }

.empty { display: flex; flex-direction: column; justify-content: center; }

.section ul { padding-left: 18px; }
.section li { font-size: 12px; color: #b0bec5; line-height: 1.8; }

/* ========== Z-tier mgmt buttons ========== */
.mgmt-row { display: flex; gap: 8px; margin-top: 4px; flex-wrap: wrap; }

.btn {
  font-size: 12px; padding: 7px 14px; border-radius: 6px; border: none;
  cursor: pointer; font-family: inherit; transition: all .15s;
}
.btn:disabled { opacity: .5; cursor: not-allowed; }

.btn.install { background: rgba(74,222,128,0.15); color: #4ade80; border: 1px solid rgba(74,222,128,0.3); }
.btn.install:hover:not(:disabled) { background: rgba(74,222,128,0.25); }

.btn.uninstall { background: rgba(239,68,68,0.12); color: #f87171; border: 1px solid rgba(239,68,68,0.25); }
.btn.uninstall:hover:not(:disabled) { background: rgba(239,68,68,0.22); }

.btn.status { background: rgba(251,191,36,0.12); color: #fbbf24; border: 1px solid rgba(251,191,36,0.25); }
.btn.status:hover:not(:disabled) { background: rgba(251,191,36,0.22); }
.btn.edit { background: rgba(168,85,247,0.12); color: #c084fc; border: 1px solid rgba(168,85,247,0.25); }
.btn.edit:hover:not(:disabled) { background: rgba(168,85,247,0.22); }
.btn.danger { background: rgba(220,38,38,0.15); color: #ef4444; border: 1px solid rgba(220,38,38,0.3); }
.btn.danger:hover:not(:disabled) { background: rgba(220,38,38,0.25); }

/* exec log */
.exec-log {
  margin-top: 12px; border-radius: 6px; overflow: hidden; font-size: 11px;
  background: rgba(0,0,0,0.4);
}
.exec-log.ok .log-header { background: rgba(74,222,128,0.15); color: #4ade80; }
.exec-log.fail .log-header { background: rgba(239,68,68,0.15); color: #f87171; }
.log-header { padding: 6px 10px; font-weight: bold; font-size: 11px; }
.exec-log pre {
  margin: 0; padding: 8px 12px; background: rgba(0,0,0,0.5);
  color: #d0e4f7; white-space: pre-wrap; font-family: Consolas, monospace;
  max-height: 160px; overflow: auto;
}
.exec-log .stderr { color: #f87171; border-top: 1px dashed rgba(239,68,68,0.2); }
</style>
