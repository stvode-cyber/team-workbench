<template>
  <div class="filter-bar">
    <input v-model="search" placeholder="搜员工名 / 触发词 / 核心能力..." @input="emitFilter" />
    <button
      v-for="f in filters" :key="f.key"
      class="filter-btn" :class="{ active: currentFilter === f.key }"
      @click="currentFilter = f.key; emitFilter()"
    >{{ f.label }}</button>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from "vue";

const props = defineProps<{ modelValue: string; filter: string }>();
const emit = defineEmits<{ (e: "update:modelValue", v: string): void; (e: "update:filter", v: string): void }>();

const search = ref(props.modelValue);
const currentFilter = ref(props.filter);

watch(() => props.modelValue, (v) => (search.value = v));
watch(() => props.filter, (v) => (currentFilter.value = v));

function emitFilter() {
  emit("update:modelValue", search.value);
  emit("update:filter", currentFilter.value);
}

const filters = [
  { key: "all", label: "全部" },
  { key: "P0", label: "🔴 P0" },
  { key: "P1", label: "🟡 P1" },
  { key: "installed", label: "✅ 已装" },
  { key: "not-installed", label: "❌ 未装" },
  { key: "busy", label: "🟡 忙碌" },
];
</script>
