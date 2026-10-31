<template>
  <div class="modal-overlay" @click.self="$emit('close')">
    <div class="modal">
      <div class="modal-header">
        <h2>{{ mode === "create" ? "➕ 新增员工" : "✏️ 编辑员工" }}：{{ form.cn_name || "未命名" }}</h2>
        <button class="close" @click="$emit('close')">✕</button>
      </div>

      <!-- 管制门槛（只在 create 时显示） -->
      <div v-if="mode === 'create'" class="governance">
        <div class="gov-title">🛡️ 管制协议 · 5 条强制门槛（过不了不许提交）</div>
        <div v-if="gates.length" class="gates">
          <div v-for="g in gates" :key="g.id" class="gate" :class="g.pass ? 'pass' : 'fail'">
            <span class="g-id">#{{ g.id }}</span>
            <span class="g-name">{{ g.name }}</span>
            <span class="g-status">{{ g.pass ? '✅ PASS' : '❌ FAIL' }}</span>
            <span class="g-detail" v-if="g.detail">{{ g.detail }}</span>
          </div>
        </div>
        <div v-else class="gates hint">填完基本字段自动跑门槛...</div>
      </div>

      <form class="form" @submit.prevent="onSubmit">
        <!-- 基本信息 -->
        <div class="form-row">
          <label>英文名 *</label>
          <input v-model="form.name" placeholder="kebab-case, 如 release-automator" :disabled="mode === 'edit'" />
        </div>
        <div class="form-row">
          <label>中文名 *</label>
          <input v-model="form.cn_name" placeholder="如 发布自动化员" />
        </div>
        <div class="form-row two">
          <div>
            <label>优先级 *</label>
            <select v-model="form.priority">
              <option value="P0">🔴 P0 核心岗</option>
              <option value="P1">🟡 P1 支援岗</option>
            </select>
          </div>
          <div>
            <label>Emoji</label>
            <input v-model="form.emoji" placeholder="🤖" maxlength="2" />
          </div>
        </div>
        <div class="form-row">
          <label>一句话描述</label>
          <input v-model="form.desc" placeholder="做什么用的" />
        </div>

        <!-- 详细 -->
        <div class="form-row">
          <label>Role *（角色职责，≥10字）</label>
          <textarea v-model="form.role" rows="2" placeholder="具体干啥、铁律是什么"></textarea>
        </div>
        <div class="form-row">
          <label>手动触发词 *（逗号分隔）</label>
          <input v-model="manualStr" placeholder="推演一下, 对比方案, 正反推" />
        </div>
        <div class="form-row">
          <label>自动触发 *（逗号分隔）</label>
          <input v-model="autoStr" placeholder="改≥3行代码自动触发, 花钱/发布前自动触发" />
        </div>
        <div class="form-row">
          <label>工作流程（逗号分隔）</label>
          <textarea v-model="processStr" rows="2" placeholder="先A, 再B, 最后C"></textarea>
        </div>
        <div class="form-row">
          <label>硬性边界 *（逗号分隔）</label>
          <textarea v-model="boundaryStr" rows="2" placeholder="不许X, 不许Y"></textarea>
        </div>
        <div class="form-row two">
          <div>
            <label>拥有工具（逗号分隔）</label>
            <input v-model="toolsStr" placeholder="read, search, web_search" />
          </div>
          <div>
            <label>会调谁（逗号分隔）</label>
            <input v-model="dependsStr" placeholder="competitor-surveyor" />
          </div>
        </div>

        <!-- 提交 -->
        <div class="form-actions">
          <button type="button" class="btn cancel" @click="$emit('close')">取消</button>
          <button type="submit" class="btn submit" :disabled="submitting">
            {{ mode === "create" ? "🚀 提交（过管制门槛才会写盘）" : "💾 保存" }}
            {{ submitting ? "..." : "" }}
          </button>
        </div>

        <!-- 结果 -->
        <div v-if="result" class="form-result" :class="result.ok ? 'ok' : 'fail'">
          <div v-if="result.ok">
            ✅ 成功！{{ mode === "create" ? `已创建 · DB id=${result.gates?.length ? "Gates全过" : ""}` : "已更新" }}
          </div>
          <div v-else>❌ 失败：{{ result.error || "检查管制门槛" }}</div>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, watch, computed } from "vue";
import { createAgent, updateAgent, type GateCheck, type CreateAgentPayload } from "../api/agents";

const props = defineProps<{
  mode: "create" | "edit";
  initial?: Partial<CreateAgentPayload> | null;
}>();

const emit = defineEmits<{ (e: "close"): void; (e: "saved"): void }>();

const form = reactive<CreateAgentPayload>({
  name: "", cn_name: "", priority: "P1", emoji: "🤖",
  desc: "", role: "",
  how_to_trigger: { manual: [], auto: [] },
  process: [], boundary: [], tools: [], depends_on: [],
});

const manualStr = ref("");
const autoStr = ref("");
const processStr = ref("");
const boundaryStr = ref("");
const toolsStr = ref("");
const dependsStr = ref("");

const submitting = ref(false);
const gates = ref<GateCheck[]>([]);
const result = ref<{ ok: boolean; error?: string; gates?: GateCheck[] } | null>(null);

// Load initial for edit mode
watch(() => props.initial, (init) => {
  if (init) {
    Object.assign(form, init);
    manualStr.value = (init.how_to_trigger?.manual || []).join(", ");
    autoStr.value = (init.how_to_trigger?.auto || []).join(", ");
    processStr.value = (init.process || []).join(", ");
    boundaryStr.value = (init.boundary || []).join(", ");
    toolsStr.value = (init.tools || []).join(", ");
    dependsStr.value = (init.depends_on || []).join(", ");
  }
}, { immediate: true });

function syncForm() {
  form.how_to_trigger = {
    manual: manualStr.value.split(/[,，]/).map((s) => s.trim()).filter(Boolean),
    auto: autoStr.value.split(/[,，]/).map((s) => s.trim()).filter(Boolean),
  };
  form.process = processStr.value.split(/[,，]/).map((s) => s.trim()).filter(Boolean);
  form.boundary = boundaryStr.value.split(/[,，]/).map((s) => s.trim()).filter(Boolean);
  form.tools = toolsStr.value.split(/[,，]/).map((s) => s.trim()).filter(Boolean);
  form.depends_on = dependsStr.value.split(/[,，]/).map((s) => s.trim()).filter(Boolean);
}

async function runPreCheck() {
  if (props.mode !== "create") return;
  syncForm();
  gates.value = [];
  try {
    // Send to backend but don't let it create — check gates manually?
    // Actually we let POST run and get 422 with gates if fail
    const r = await createAgent(form);
    if (r.ok) {
      result.value = r;
      gates.value = r.gates || [];
      emit("saved");
      setTimeout(() => emit("close"), 1500);
    } else {
      result.value = r;
      gates.value = r.gates || [];
    }
  } catch (e: any) {
    result.value = { ok: false, error: e?.message || String(e) };
  } finally {
    submitting.value = false;
  }
}

async function onSubmit() {
  result.value = null;
  submitting.value = true;
  syncForm();

  if (props.mode === "create") {
    await runPreCheck();
  } else {
    // Edit mode: PUT (no governance)
    try {
      const name = props.initial?.name;
      if (!name) { result.value = { ok: false, error: "no name" }; return; }
      const r = await updateAgent(name, form);
      result.value = r;
      emit("saved");
      setTimeout(() => emit("close"), 1000);
    } catch (e: any) {
      result.value = { ok: false, error: e?.message || String(e) };
    } finally {
      submitting.value = false;
    }
  }
}
</script>

<style scoped>
.modal-overlay {
  position: fixed; inset: 0; background: rgba(0,0,0,0.6);
  display: flex; align-items: center; justify-content: center;
  z-index: 1000;
}
.modal {
  background: #1a202c; border-radius: 12px;
  width: 620px; max-height: 88vh; overflow-y: auto;
  border: 1px solid rgba(255,255,255,0.1);
}
.modal-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 14px 20px; border-bottom: 1px solid rgba(255,255,255,0.08);
}
.modal-header h2 { font-size: 14px; color: #fff; }
.close { background: none; border: none; color: #888; font-size: 18px; cursor: pointer; }
.close:hover { color: #fff; }

.governance {
  padding: 12px 20px; border-bottom: 1px solid rgba(255,255,255,0.08);
  background: rgba(100,200,255,0.04);
}
.gov-title { font-size: 11px; color: var(--muted); margin-bottom: 8px; }
.gate {
  display: grid; grid-template-columns: 30px 1fr 70px;
  gap: 8px; font-size: 11px; padding: 4px 0;
}
.gate.pass .g-status { color: #4ade80; }
.gate.fail .g-status { color: #f87171; }
.gate.fail { color: #f87171; }
.g-id { color: var(--muted); }
.g-detail { grid-column: 2 / -1; color: var(--muted); font-size: 10px; padding-left: 30px; }
.gates.hint { color: var(--muted); font-size: 11px; }

.form { padding: 16px 20px; display: flex; flex-direction: column; gap: 10px; }
.form-row { display: flex; flex-direction: column; gap: 4px; }
.form-row.two { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
.form-row label { font-size: 11px; color: var(--muted); }
.form-row input, .form-row textarea, .form-row select {
  background: rgba(0,0,0,0.3); border: 1px solid rgba(255,255,255,0.08);
  color: #fff; border-radius: 6px; padding: 7px 10px; font-size: 12px;
  font-family: inherit; resize: vertical;
}
.form-row textarea { min-height: 48px; }
.form-row input:disabled { opacity: .4; }

.form-actions { display: flex; gap: 8px; justify-content: flex-end; margin-top: 4px; }
.btn { padding: 7px 16px; border-radius: 6px; border: none; cursor: pointer; font-size: 12px; font-family: inherit; }
.btn.cancel { background: rgba(255,255,255,0.08); color: #aaa; }
.btn.submit { background: rgba(96,165,250,0.2); color: #60a5fa; border: 1px solid rgba(96,165,250,0.3); }
.btn.submit:disabled { opacity: .5; cursor: not-allowed; }

.form-result { padding: 10px; border-radius: 6px; font-size: 12px; }
.form-result.ok { background: rgba(74,222,128,0.1); color: #4ade80; }
.form-result.fail { background: rgba(239,68,68,0.1); color: #f87171; }
</style>
