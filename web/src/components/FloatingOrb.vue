<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from "vue";
import type { AgentListResponse } from "../api/agents";

const props = defineProps<{
  summary?: AgentListResponse["summary"] | null;
}>();
const emit = defineEmits<{
  (e: "toggle"): void;
}>();

// 位置（localStorage 记忆）
const DEFAULT_POS = { x: -1, y: -1 };
const pos = ref<{ x: number; y: number }>({ ...DEFAULT_POS });
const dragging = ref(false);
const dragOffset = { x: 0, y: 0 };
let clickBlocked = false;

// 状态灯：🟢 全绿 / 🟡 有忙碌 / 🔴 有未装
const badgeClass = computed(() => {
  const s = props.summary;
  if (!s) return "badge green";
  if (s.busy > 0) return "badge yellow";
  if (s.missing > 0) return "badge red";
  return "badge green";
});

const badgeIcon = computed(() => {
  const s = props.summary;
  if (!s) return "🟢";
  if (s.busy > 0) return "🟡";
  if (s.missing > 0) return "🔴";
  return "🟢";
});

function onPointerDown(e: PointerEvent) {
  dragging.value = true;
  clickBlocked = false;
  dragOffset.x = e.clientX - (pos.value.x === -1 ? window.innerWidth - 90 : pos.value.x);
  dragOffset.y = e.clientY - (pos.value.y === -1 ? window.innerHeight - 100 : pos.value.y);
  (e.target as HTMLElement).setPointerCapture(e.pointerId);
}

function onPointerMove(e: PointerEvent) {
  if (!dragging.value) return;
  clickBlocked = true;
  let nx = e.clientX - dragOffset.x;
  let ny = e.clientY - dragOffset.y;

  // 吸屏：边缘 40px 内自动吸附
  const orbSize = 64;
  if (nx < 40) nx = 0;
  if (nx > window.innerWidth - orbSize - 40) nx = window.innerWidth - orbSize;
  if (ny < 40) ny = 0;
  if (ny > window.innerHeight - orbSize - 40) ny = window.innerHeight - orbSize;

  pos.value = { x: nx, y: ny };
}

function onPointerUp(e: PointerEvent) {
  dragging.value = false;
  try {
    (e.target as HTMLElement).releasePointerCapture(e.pointerId);
  } catch {}
  // 保存位置
  try {
    localStorage.setItem("float-orb-pos", JSON.stringify(pos.value));
  } catch {}
}

function onClick() {
  if (clickBlocked) {
    clickBlocked = false;
    return;
  }
  emit("toggle");
}

// 双击：打开主面板
let lastClick = 0;
function onDblClick() {
  const now = Date.now();
  if (now - lastClick < 300) {
    window.open("/", "_blank");
  }
  lastClick = now;
}

onMounted(() => {
  try {
    const saved = localStorage.getItem("float-orb-pos");
    if (saved) {
      const p = JSON.parse(saved);
      pos.value = { x: p.x, y: p.y };
    }
  } catch {}
  // 默认右下角
  if (pos.value.x === -1) {
    pos.value = { x: window.innerWidth - 90, y: window.innerHeight - 140 };
  }
  window.addEventListener("pointermove", onPointerMove);
  window.addEventListener("pointerup", onPointerUp);
  window.addEventListener("pointercancel", onPointerUp);
});

onBeforeUnmount(() => {
  window.removeEventListener("pointermove", onPointerMove);
  window.removeEventListener("pointerup", onPointerUp);
  window.removeEventListener("pointercancel", onPointerUp);
});
</script>

<template>
  <div
    class="float-orb"
    :class="{ dragging }"
    :style="{ left: pos.x + 'px', top: pos.y + 'px' }"
    @pointerdown="onPointerDown"
    @click="onClick"
    @dblclick="onDblClick"
    title="AI 专岗员工 · 点击展开 · 双击打开主面板"
  >
    <span class="orb-icon">🤖</span>
    <span :class="badgeClass">{{ badgeIcon }}</span>
  </div>
</template>

<style scoped>
.float-orb {
  position: fixed;
  width: 64px;
  height: 64px;
  border-radius: 50%;
  background: radial-gradient(circle at 30% 30%, #64c8ff 0%, #6366f1 60%, #3730a3 100%);
  box-shadow: 0 8px 24px rgba(99, 102, 241, 0.5), 0 2px 6px rgba(0,0,0,0.3);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  z-index: 9998;
  transition: transform 0.2s, box-shadow 0.2s;
  user-select: none;
}
.float-orb:hover {
  transform: scale(1.08);
  box-shadow: 0 12px 32px rgba(99, 102, 241, 0.6);
}
.float-orb.dragging {
  transform: scale(0.95);
  cursor: grabbing;
  transition: none;
}
.orb-icon {
  font-size: 28px;
  filter: drop-shadow(0 2px 4px rgba(0,0,0,0.4));
}
.badge {
  position: absolute;
  top: -4px;
  right: -4px;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  font-size: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #1a1a2e;
  border: 2px solid #1a1a2e;
}
.badge.green { background: var(--green); }
.badge.yellow { background: var(--yellow); animation: pulse 1.5s infinite; }
.badge.red { background: var(--red); }
</style>
