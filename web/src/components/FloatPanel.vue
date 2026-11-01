<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from "vue";
import type { Agent, AgentListResponse } from "../api/agents";

const props = defineProps<{
  agents: Agent[];
  summary?: AgentListResponse["summary"] | null;
  modelValue?: Agent | null;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", a: Agent | null): void;
  (e: "select", a: Agent): void;
  (e: "close"): void;
  (e: "scan-all"): void;
  (e: "install-all"): void;
}>();

// 搜索
const keyword = ref("");
// 默认选中第一个
const selected = ref<Agent | null>(props.modelValue || props.agents[0] || null);

const filteredAgents = computed(() => {
  const kw = keyword.value.trim().toLowerCase();
  if (!kw) return props.agents;
  return props.agents.filter(
    (a) =>
      a.cn_name.toLowerCase().includes(kw) ||
      a.name.toLowerCase().includes(kw) ||
      a.emoji.includes(kw)
  );
});

const selectedAgent = computed(() => {
  return selected.value && props.agents.find((a) => a.name === selected.value!.name)
    ? props.agents.find((a) => a.name === selected.value!.name)!
    : props.agents[0] || null;
});

function onSelect(a: Agent) {
  selected.value = a;
  emit("select", a);
}

function onEsc(e: KeyboardEvent) {
  if (e.key === "Escape") emit("close");
}

onMounted(() => {
  window.addEventListener("keydown", onEsc);
});
onBeforeUnmount(() => {
  window.removeEventListener("keydown", onEsc);
});

function statusIcon(a: Agent) {
  if (a.runtime_status === "busy") return "🟡";
  if (a.install_status === "installed") return "🟢";
  if (a.install_status === "partial") return "🟡";
  return "🔴";
}

function statusLabel(a: Agent) {
  if (a.runtime_status === "busy") return "忙碌";
  if (a.install_status === "installed") return "待命";
  if (a.install_status === "partial") return "部分安装";
  return "未安装";
}
</script>

<template>
  <Teleport to="body">
    <div class="float-mask" @click.self="emit('close')"></div>
    <div class="float-panel">
      <!-- 顶部 -->
      <div class="fp-header">
        <div class="fp-title">
          🤖 AI 专岗员工
          <span class="fp-stat">
            {{ summary?.installed || 0 }}/{{ summary?.total || 0 }} 已装 ·
            {{ summary?.busy || 0 }} 忙碌
          </span>
        </div>
        <button class="fp-close" @click="emit('close')">×</button>
      </div>

      <!-- 左栏：Agent 列表 -->
      <div class="fp-body">
        <div class="fp-list">
          <input
            v-model="keyword"
            class="fp-search"
            placeholder="🔍 搜索专岗..."
          />
          <div class="fp-items">
            <div
              v-for="a in filteredAgents"
              :key="a.name"
              class="fp-item"
              :class="{ active: selectedAgent?.name === a.name }"
              @click="onSelect(a)"
            >
              <span class="fp-avatar">{{ a.emoji }}</span>
              <div class="fp-item-info">
                <div class="fp-cn">{{ a.cn_name }}</div>
                <div class="fp-en">{{ a.name }}</div>
              </div>
              <span class="fp-dot" :class="a.runtime_status === 'busy' ? 'busy' : 'idle'"></span>
            </div>
            <div v-if="filteredAgents.length === 0" class="fp-empty">
              没找到相关专岗
            </div>
          </div>
        </div>

        <!-- 右栏：资料详情 -->
        <div class="fp-detail" v-if="selectedAgent">
          <div class="fp-detail-header">
            <span class="fp-big-emoji">{{ selectedAgent.emoji }}</span>
            <div class="fp-detail-meta">
              <div class="fp-detail-name">{{ selectedAgent.cn_name }}</div>
              <div class="fp-detail-tags">
                <span :class="'pri-tag pri-' + selectedAgent.priority.toLowerCase()">
                  {{ selectedAgent.priority }}
                </span>
                <span :class="'status-tag status-' + selectedAgent.runtime_status">
                  {{ statusLabel(selectedAgent) }}
                </span>
              </div>
            </div>
            <span class="fp-detail-status-icon">{{ statusIcon(selectedAgent) }}</span>
          </div>

          <div class="fp-sections">
            <div class="fp-section" v-if="selectedAgent.desc">
              <div class="fp-sec-title">📋 一句话说明</div>
              <div class="fp-sec-body">{{ selectedAgent.desc }}</div>
            </div>

            <div class="fp-section" v-if="selectedAgent.how_to_trigger?.manual?.length">
              <div class="fp-sec-title">🎯 什么时候调我</div>
              <ul class="fp-sec-list">
                <li v-for="(t, i) in selectedAgent.how_to_trigger.manual" :key="i">{{ t }}</li>
              </ul>
            </div>

            <div class="fp-section" v-if="selectedAgent.process?.length">
              <div class="fp-sec-title">⚙️ 工作流程</div>
              <ul class="fp-sec-list">
                <li v-for="(p, i) in selectedAgent.process" :key="i">{{ i + 1 }}. {{ p }}</li>
              </ul>
            </div>

            <div class="fp-section" v-if="selectedAgent.boundary?.length">
              <div class="fp-sec-title">🚫 硬性边界</div>
              <ul class="fp-sec-list danger">
                <li v-for="(b, i) in selectedAgent.boundary" :key="i">{{ b }}</li>
              </ul>
            </div>

            <div class="fp-section" v-if="selectedAgent.tools?.length">
              <div class="fp-sec-title">🛠️ 拥有工具</div>
              <div class="fp-tools">
                <span v-for="t in selectedAgent.tools" :key="t" class="fp-tool">{{ t }}</span>
              </div>
            </div>
          </div>
        </div>

        <!-- 右栏空态 -->
        <div v-else class="fp-detail-empty">请选择左侧专岗</div>
      </div>

      <!-- 底部快捷栏 -->
      <div class="fp-footer">
        <button class="fp-btn scan" @click="emit('scan-all')">🔄 全扫状态</button>
        <button class="fp-btn install" @click="emit('install-all')">🚀 一键全装</button>
        <span class="fp-tip">💡 双击悬浮球打开主面板</span>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.float-mask {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.35);
  backdrop-filter: blur(2px);
  z-index: 9997;
}
.float-panel {
  position: fixed;
  right: 100px;
  bottom: 100px;
  width: 620px;
  height: 520px;
  background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
  border: 1px solid rgba(100, 200, 255, 0.25);
  border-radius: 14px;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.6), 0 0 40px rgba(100, 200, 255, 0.1);
  display: flex;
  flex-direction: column;
  z-index: 9999;
  overflow: hidden;
}

/* 顶部 */
.fp-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 18px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(255, 255, 255, 0.03);
}
.fp-title {
  font-size: 15px;
  color: #fff;
  font-weight: 600;
  display: flex;
  align-items: center;
  gap: 10px;
}
.fp-stat {
  font-size: 11px;
  color: var(--muted);
  font-weight: 400;
  background: rgba(100, 200, 255, 0.1);
  padding: 3px 8px;
  border-radius: 4px;
}
.fp-close {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  border: none;
  background: rgba(255, 255, 255, 0.08);
  color: var(--muted);
  font-size: 16px;
  cursor: pointer;
  transition: all 0.2s;
}
.fp-close:hover {
  background: var(--red);
  color: #fff;
}

/* 主体：两栏 */
.fp-body {
  flex: 1;
  display: flex;
  overflow: hidden;
}
.fp-list {
  width: 200px;
  border-right: 1px solid rgba(255, 255, 255, 0.08);
  display: flex;
  flex-direction: column;
  background: rgba(0, 0, 0, 0.2);
}
.fp-detail {
  flex: 1;
  overflow-y: auto;
  padding: 16px 20px;
}

/* 搜索 */
.fp-search {
  margin: 10px 12px;
  padding: 7px 12px;
  background: rgba(255, 255, 255, 0.06);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  color: #fff;
  font-size: 12px;
  outline: none;
}
.fp-search:focus {
  border-color: var(--accent);
}

/* 列表项 */
.fp-items {
  flex: 1;
  overflow-y: auto;
  padding: 0 6px;
}
.fp-item {
  display: flex;
  align-items: center;
  padding: 9px 10px;
  border-radius: 6px;
  cursor: pointer;
  transition: background 0.15s;
  gap: 8px;
}
.fp-item:hover {
  background: rgba(100, 200, 255, 0.08);
}
.fp-item.active {
  background: rgba(100, 200, 255, 0.18);
  border-left: 2px solid var(--accent);
}
.fp-avatar {
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  flex-shrink: 0;
}
.fp-item-info {
  flex: 1;
  min-width: 0;
}
.fp-cn {
  font-size: 12px;
  color: #fff;
  font-weight: 500;
}
.fp-en {
  font-size: 9px;
  color: var(--muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.fp-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}
.fp-dot.idle { background: var(--green); box-shadow: 0 0 4px var(--green); }
.fp-dot.busy { background: var(--yellow); box-shadow: 0 0 4px var(--yellow); animation: pulse 1.5s infinite; }
.fp-empty {
  text-align: center;
  color: var(--muted);
  font-size: 11px;
  padding: 30px 0;
}

/* 右栏详情头部 */
.fp-detail-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding-bottom: 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  margin-bottom: 12px;
}
.fp-big-emoji {
  font-size: 42px;
  width: 56px;
  height: 56px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(100, 200, 255, 0.1);
  border-radius: 12px;
}
.fp-detail-meta { flex: 1; }
.fp-detail-name {
  font-size: 18px;
  font-weight: 600;
  color: #fff;
}
.fp-detail-tags {
  display: flex;
  gap: 6px;
  margin-top: 4px;
}
.pri-tag {
  font-size: 10px;
  padding: 2px 8px;
  border-radius: 4px;
  font-weight: 600;
}
.pri-p0 { background: rgba(248, 113, 113, 0.15); color: var(--red); }
.pri-p1 { background: rgba(251, 191, 36, 0.15); color: var(--yellow); }
.status-tag {
  font-size: 10px;
  padding: 2px 8px;
  border-radius: 4px;
}
.status-idle { background: rgba(74, 222, 128, 0.15); color: var(--green); }
.status-busy { background: rgba(251, 191, 36, 0.15); color: var(--yellow); }
.fp-detail-status-icon {
  font-size: 14px;
}

/* section */
.fp-section {
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 8px;
  padding: 10px 14px;
  margin-bottom: 8px;
}
.fp-sec-title {
  font-size: 10px;
  font-weight: 600;
  color: var(--accent);
  margin-bottom: 6px;
  letter-spacing: 0.5px;
}
.fp-sec-body {
  font-size: 12px;
  color: #b0bec5;
  line-height: 1.6;
}
.fp-sec-list {
  padding-left: 16px;
  margin: 0;
}
.fp-sec-list li {
  font-size: 11px;
  color: #b0bec5;
  line-height: 1.6;
  margin-bottom: 2px;
}
.fp-sec-list.danger li { color: #ef9a9a; }
.fp-tools {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.fp-tool {
  font-size: 10px;
  padding: 2px 8px;
  background: rgba(100, 200, 255, 0.1);
  border: 1px solid rgba(100, 200, 255, 0.2);
  border-radius: 10px;
  color: var(--accent);
}

.fp-detail-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--muted);
  font-size: 13px;
}

/* 底部 */
.fp-footer {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 16px;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(0, 0, 0, 0.25);
}
.fp-btn {
  padding: 7px 14px;
  border-radius: 6px;
  font-size: 12px;
  cursor: pointer;
  border: none;
  font-weight: 500;
  transition: all 0.15s;
}
.fp-btn.scan {
  background: rgba(100, 200, 255, 0.15);
  color: var(--accent);
  border: 1px solid rgba(100, 200, 255, 0.3);
}
.fp-btn.scan:hover { background: rgba(100, 200, 255, 0.25); }
.fp-btn.install {
  background: rgba(74, 222, 128, 0.15);
  color: var(--green);
  border: 1px solid rgba(74, 222, 128, 0.3);
}
.fp-btn.install:hover { background: rgba(74, 222, 128, 0.25); }
.fp-tip {
  font-size: 10px;
  color: var(--muted);
  margin-left: auto;
}
</style>
