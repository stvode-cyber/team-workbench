<template>
  <div class="app">
    <header class="header">
      <div>
        <h1>🤖 AI 专岗员工面板 <span class="live"><span class="live-dot"></span> WebSocket 实时</span></h1>
        <div class="subtitle">v3.0 · Node/Express/SQLite + Vue3 + WebSocket</div>
      </div>

      <div class="header-right">
        <div class="stats" v-if="summary">
          <div class="stat"><div class="num">{{ summary.total }}</div><div class="label">总数</div></div>
          <div class="stat ok"><div class="num">{{ summary.installed }}</div><div class="label">已装</div></div>
          <div class="stat warn"><div class="num">{{ summary.busy }}</div><div class="label">忙碌</div></div>
        </div>

        <div class="toolbar">
          <button
            class="tb-btn new"
            @click="showCreateForm = true"
          >➕ 新增员工</button>
          <button
            class="tb-btn scan"
            :disabled="!!toolbarLoading"
            @click="runScanAll"
          >
            {{ toolbarLoading === "scan" ? "⏳ 扫描中..." : "🔄 全扫状态" }}
          </button>
          <button
            class="tb-btn install"
            :disabled="!!toolbarLoading"
            @click="runInstallAll"
          >
            {{ toolbarLoading === "install" ? "⏳ 安装中..." : "📦 一键全装" }}
          </button>
        </div>
      </div>
    </header>

    <!-- 批量操作结果面板（折叠式） -->
    <div v-if="batchResult" class="batch-panel" :class="batchResult.ok ? 'ok' : 'fail'">
      <div class="batch-header" :class="batchResult.ok ? 'bh-ok' : 'bh-fail'">
        <span>{{ batchResult.ok ? '✅ 批量操作完成' : '❌ 部分失败' }}</span>
        <span class="batch-count">
          ok: {{ batchResult.okCount }} / fail: {{ batchResult.failCount }} / total: {{ batchResult.total }}
        </span>
        <button class="batch-close" @click="batchResult = null">✕</button>
      </div>
      <div class="batch-body">
        <div
          v-for="(r, i) in batchResult.results"
          :key="i"
          class="batch-item"
          :class="r.ok ? 'ok' : 'fail'"
        >
          <span class="b-status">{{ r.ok ? '✅' : '❌' }}</span>
          <span class="b-name">{{ r.name }}</span>
          <span class="b-detail">{{ r.ok ? `复制 ${r.copied} 个文件` : (r.reason || '失败') }}</span>
        </div>
      </div>
    </div>

    <div class="main">
      <AgentSidebar
        :agents="agents"
        :selected="selectedName"
        @select="selected = $event"
      />
      <AgentDetail
        :agent="selected"
        @delete="onDeleteAgent"
        @edit="onEditAgent"
      />
    </div>

    <!-- Create/Edit Agent Modal -->
    <AgentForm
      v-if="showCreateForm || showEditForm"
      :mode="showEditForm ? 'edit' : 'create'"
      :initial="editInitial"
      @close="showCreateForm = false; showEditForm = false"
      @saved="onFormSaved"
    />

    <!-- Delete Confirm -->
    <div v-if="showDeleteConfirm" class="modal-overlay" @click.self="showDeleteConfirm = false">
      <div class="confirm-modal">
        <h3>🗑️ 确认删除「{{ deleteTarget?.cn_name }}」？</h3>
        <p class="warn">将同时删除：源目录 agent.md + SKILL.md + 安装文件 + DB 记录。<br/>不可恢复。</p>
        <div class="confirm-actions">
          <button class="btn cancel" @click="showDeleteConfirm = false">取消</button>
          <button class="btn danger" @click="doDelete">确认删除</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from "vue";
import { fetchAgents, scanAllAgents, installAllAgents, deleteAgent, type Agent, type AgentListResponse } from "./api/agents";
import { connectWs, closeWs } from "./api/ws";
import AgentSidebar from "./components/AgentSidebar.vue";
import AgentDetail from "./components/AgentDetail.vue";
import AgentForm from "./components/AgentForm.vue";

const agents = ref<Agent[]>([]);
const summary = ref<AgentListResponse["summary"] | null>(null);
const selected = ref<Agent | null>(null);
const selectedName = ref<string>("");
const toolbarLoading = ref<"" | "scan" | "install">("");

interface BatchResultItem { name: string; ok: boolean; copied?: number; reason?: string; install_status?: string; }
const batchResult = ref<{ ok: boolean; okCount: number; failCount: number; total: number; results: BatchResultItem[] } | null>(null);

// CRUD modal state
const showCreateForm = ref(false);
const showEditForm = ref(false);
const editInitial = ref<Partial<Agent> | null>(null);
const showDeleteConfirm = ref(false);
const deleteTarget = ref<Agent | null>(null);

onMounted(async () => {
  await load();

  if (agents.value.length > 0) {
    selected.value = agents.value[0];
    selectedName.value = agents.value[0].name;
  }

  // WebSocket 实时更新（3 种事件）
  connectWs(
    // scan event → install_status
    (name, data) => {
      const a = agents.value.find((x) => x.name === name);
      if (a && data) {
        a.install_status = data.install_status ?? a.install_status;
        a.install_date = data.install_date ?? a.install_date;
        summary.value = recomputeSummary();
      }
    },
    // runtime event → runtime_status
    (name, runtimeStatus) => {
      const a = agents.value.find((x) => x.name === name);
      if (a) {
        a.runtime_status = runtimeStatus as Agent["runtime_status"];
        summary.value = recomputeSummary();
      }
    },
    // install event → same as scan
    (name, data) => {
      const a = agents.value.find((x) => x.name === name);
      if (a && data) {
        a.install_status = data.install_status ?? a.install_status;
        summary.value = recomputeSummary();
      }
    }
  );
});

onUnmounted(() => closeWs());

async function load() {
  try {
    const res = await fetchAgents();
    agents.value = res.agents;
    summary.value = res.summary;
  } catch (e) {
    console.error("load failed", e);
  }
}

function recomputeSummary(): AgentListResponse["summary"] {
  return {
    total: agents.value.length,
    installed: agents.value.filter((a) => a.install_status === "installed").length,
    partial: agents.value.filter((a) => a.install_status === "partial").length,
    missing: agents.value.filter((a) => a.install_status !== "installed" && a.install_status !== "partial").length,
    busy: agents.value.filter((a) => a.runtime_status === "busy").length,
  };
}

async function runScanAll() {
  toolbarLoading.value = "scan";
  batchResult.value = null;
  try {
    const r = await scanAllAgents();
    // reload full list after scan
    await load();
    batchResult.value = {
      ok: true,
      okCount: r.ok ? 11 : 0,
      failCount: r.ok ? 0 : 11,
      total: 11,
      results: agents.value.map((a) => ({
        name: a.cn_name,
        ok: a.install_status === "installed" || a.install_status === "partial",
        install_status: a.install_status,
      })),
    };
  } catch (e: any) {
    batchResult.value = {
      ok: false, okCount: 0, failCount: 11, total: 11,
      results: agents.value.map((a) => ({ name: a.cn_name, ok: false, reason: e?.message || "scan failed" })),
    };
  } finally {
    toolbarLoading.value = "";
  }
}

async function runInstallAll() {
  toolbarLoading.value = "install";
  batchResult.value = null;
  try {
    const r = await installAllAgents();
    await load();
    batchResult.value = {
      ok: r.fail === 0,
      okCount: r.ok,
      failCount: r.fail,
      total: r.total,
      results: r.results,
    };
  } catch (e: any) {
    batchResult.value = {
      ok: false, okCount: 0, failCount: 11, total: 11,
      results: agents.value.map((a) => ({ name: a.cn_name, ok: false, reason: e?.message || "install failed" })),
    };
  } finally {
    toolbarLoading.value = "";
  }
}

// ========== CRUD ==========
function onEditAgent(agent: Agent | null) {
  if (!agent) return;
  editInitial.value = { ...agent };
  showEditForm.value = true;
}

function onDeleteAgent(agent: Agent | null) {
  if (!agent) return;
  deleteTarget.value = agent;
  showDeleteConfirm.value = true;
}

async function doDelete() {
  if (!deleteTarget.value) return;
  const r = await deleteAgent(deleteTarget.value.name);
  if (r.ok) {
    agents.value = agents.value.filter((a) => a.name !== deleteTarget.value!.name);
    if (selected.value?.name === deleteTarget.value.name) {
      selected.value = agents.value[0] || null;
      selectedName.value = selected.value?.name || "";
    }
    summary.value = recomputeSummary();
  }
  showDeleteConfirm.value = false;
  deleteTarget.value = null;
}

async function onFormSaved() {
  showCreateForm.value = false;
  showEditForm.value = false;
  await load();
  // keep selection
  const cur = selectedName.value;
  selected.value = agents.value.find((a) => a.name === cur) || agents.value[0] || null;
  selectedName.value = selected.value?.name || "";
}

watch(selected, (a) => { selectedName.value = a?.name ?? ""; });
</script>

<style scoped>
.header-right { display: flex; align-items: center; gap: 16px; }
.stats { display: flex; gap: 12px; }
.stat { text-align: center; }
.stat .num { font-size: 20px; font-weight: 700; color: #fff; }
.stat .label { font-size: 10px; color: var(--muted); }
.ok .num { color: var(--green); }
.warn .num { color: var(--yellow); }

.live { font-size: 10px; color: var(--muted); margin-left: 10px; display: inline-flex; align-items: center; gap: 6px; }
.live-dot { width: 6px; height: 6px; border-radius: 50%; background: var(--green); animation: pulse 2s infinite; }

/* toolbar */
.toolbar { display: flex; gap: 6px; }
.tb-btn {
  font-size: 12px; padding: 7px 14px; border-radius: 6px; border: none;
  cursor: pointer; font-family: inherit; transition: all .15s; white-space: nowrap;
}
.tb-btn:disabled { opacity: .5; cursor: not-allowed; }
.tb-btn.scan { background: rgba(96,165,250,0.12); color: #60a5fa; border: 1px solid rgba(96,165,250,0.25); }
.tb-btn.scan:hover:not(:disabled) { background: rgba(96,165,250,0.25); }
.tb-btn.install { background: rgba(74,222,128,0.12); color: #4ade80; border: 1px solid rgba(74,222,128,0.25); }
.tb-btn.install:hover:not(:disabled) { background: rgba(74,222,128,0.25); }
.tb-btn.new { background: rgba(251,191,36,0.12); color: #fbbf24; border: 1px solid rgba(251,191,36,0.25); }
.tb-btn.new:hover:not(:disabled) { background: rgba(251,191,36,0.25); }

/* confirm modal */
.confirm-modal {
  background: #1a202c; border-radius: 10px; padding: 20px; width: 380px;
  border: 1px solid rgba(255,255,255,0.1);
}
.confirm-modal h3 { color: #fff; font-size: 14px; margin-bottom: 8px; }
.confirm-modal .warn { color: #f87171; font-size: 11px; line-height: 1.7; }
.confirm-actions { display: flex; gap: 8px; justify-content: flex-end; margin-top: 14px; }
.confirm-actions .btn.cancel { background: rgba(255,255,255,0.08); color: #aaa; border: none; padding: 6px 14px; border-radius: 6px; cursor: pointer; }
.confirm-actions .btn.danger { background: rgba(239,68,68,0.2); color: #f87171; border: 1px solid rgba(239,68,68,0.3); padding: 6px 14px; border-radius: 6px; cursor: pointer; }

/* batch panel */
.batch-panel {
  margin: 0 16px 8px 16px; border-radius: 8px; overflow: hidden;
  border: 1px solid rgba(255,255,255,0.08);
}
.batch-panel.ok { border-color: rgba(74,222,128,0.3); background: rgba(74,222,128,0.06); }
.batch-panel.fail { border-color: rgba(239,68,68,0.3); background: rgba(239,68,68,0.06); }

.batch-header {
  display: flex; align-items: center; gap: 12px; padding: 8px 14px; font-size: 12px; font-weight: bold;
}
.bh-ok { background: rgba(74,222,128,0.12); color: #4ade80; }
.bh-fail { background: rgba(239,68,68,0.12); color: #f87171; }
.batch-count { font-size: 11px; opacity: .8; }
.batch-close { margin-left: auto; background: none; border: none; color: inherit; cursor: pointer; font-size: 14px; }

.batch-body { padding: 8px 14px 12px; max-height: 240px; overflow-y: auto; }
.batch-item {
  display: flex; align-items: center; gap: 10px; font-size: 11px; padding: 3px 0;
  border-bottom: 1px dashed rgba(255,255,255,0.06);
}
.batch-item:last-child { border-bottom: none; }
.batch-item.ok .b-name { color: #4ade80; }
.batch-item.fail .b-name { color: #f87171; }
.b-status { width: 18px; text-align: center; }
.b-name { font-weight: bold; min-width: 100px; }
.b-detail { color: var(--muted); font-family: Consolas, monospace; font-size: 10px; }
</style>
