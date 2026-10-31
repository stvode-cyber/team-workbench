<template>
  <div class="sidebar">
    <SearchBar v-model="search" :filter="filter" @update:filter="filter = $event" />
    <div v-for="group in grouped" :key="group.priority">
      <div class="group-label">{{ group.priority === "P0" ? "🔴 P0 核心岗" : "🟡 P1 支援岗" }} <span class="count">{{ group.list.length }}</span></div>
      <div
        v-for="a in group.list" :key="a.name"
        class="agent-item" :class="{ active: a.name === selected }"
        @click="$emit('select', a)"
      >
        <div class="agent-avatar">{{ a.emoji }}</div>
        <div class="agent-info">
          <div class="agent-cn">{{ a.cn_name }} <span class="agent-en">{{ a.name }}</span></div>
        </div>
        <div class="dot-group" :title="`运行: ${a.runtime_status} | 安装: ${a.install_status}`">
          <div class="dot" :class="a.runtime_status"></div>
          <div class="dot" :class="a.install_status === 'installed' ? 'installed' : (a.install_status === 'partial' ? 'partial' : 'not-installed')"></div>
        </div>
      </div>
    </div>
    <div v-if="filtered.length === 0" style="padding: 40px; text-align: center; color: var(--muted); font-size: 12px;">
      没找到匹配的员工
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { Agent } from "../api/agents";
import SearchBar from "./SearchBar.vue";

const props = defineProps<{ agents: Agent[]; selected?: string }>();
defineEmits<{ (e: "select", a: Agent): void }>();

const search = ref("");
const filter = ref("all");

watch(() => props.agents, () => { /* reactive via prop */ }, { deep: true });

const filtered = computed(() => {
  let list = props.agents;

  // text search
  const q = search.value.trim().toLowerCase();
  if (q) {
    list = list.filter((a) =>
      a.cn_name.includes(q) ||
      a.name.toLowerCase().includes(q) ||
      a.priority.toLowerCase().includes(q)
    );
  }

  // status filter
  switch (filter.value) {
    case "P0": list = list.filter((a) => a.priority === "P0"); break;
    case "P1": list = list.filter((a) => a.priority === "P1"); break;
    case "installed": list = list.filter((a) => a.install_status === "installed"); break;
    case "not-installed": list = list.filter((a) => a.install_status !== "installed"); break;
    case "busy": list = list.filter((a) => a.runtime_status === "busy"); break;
  }
  return list;
});

const grouped = computed(() => {
  const g: Record<string, Agent[]> = {};
  for (const a of filtered.value) {
    (g[a.priority] = g[a.priority] || []).push(a);
  }
  return Object.entries(g).map(([priority, list]) => ({ priority, list }));
});
</script>

<style scoped>
.group-label { padding: 12px 20px 6px; font-size: 11px; color: var(--muted); text-transform: uppercase; letter-spacing: 1px; }
.group-label .count { background: rgba(100,200,255,0.2); color: var(--accent); font-size: 9px; padding: 1px 6px; border-radius: 10px; margin-left: 6px; font-weight: 400; letter-spacing: 0; }
</style>
