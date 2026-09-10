<template>
  <div class="page-container">
    <div class="page-header">
      <div>
        <h2>🤖 {{ assistant?.assistantName || '智能助手' }}</h2>
        <div style="font-size:12px;color:#888;margin-top:4px">{{ assistant?.description || '' }}</div>
      </div>
      <div style="display:flex;gap:8px;align-items:center">
        <span style="font-size:12px;color:#888">模型:</span>
        <el-select v-model="providerId" size="small" style="width:150px" @change="onProviderChange">
          <el-option v-for="p in providers" :key="p.id" :label="p.providerName" :value="p.id" />
        </el-select>
        <el-select v-model="currentModel" size="small" style="width:170px" filterable allow-create default-first-option>
          <el-option v-for="m in modelOptions" :key="m" :label="m" :value="m" />
        </el-select>
      </div>
    </div>

    <el-card style="margin-bottom:12px" shadow="never">
      <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end">
        <div v-for="p in inputParams" :key="p.name" style="display:flex;flex-direction:column;gap:2px">
          <span style="font-size:12px;color:#666">{{ p.label || p.name }}</span>
          <el-input v-if="!p.type || p.type==='text'" v-model="inputs[p.name]" size="small" style="width:170px" clearable />
          <el-input-number v-else-if="p.type==='number'" v-model="inputs[p.name]" size="small" style="width:170px" :controls="false" />
          <el-date-picker v-else-if="p.type==='date'" v-model="inputs[p.name]" size="small" type="date" value-format="YYYY-MM-DD" style="width:170px" />
          <el-switch v-else-if="p.type==='switch'" v-model="inputs[p.name]" size="small" />
          <el-select v-else-if="p.type==='select'" v-model="inputs[p.name]" size="small" style="width:170px" clearable>
            <el-option v-for="(o, i) in selectOptions(p)" :key="i" :label="o" :value="o" />
          </el-select>
        </div>
      </div>
      <el-input v-model="extraText" type="textarea" :rows="3" style="margin-top:10px" placeholder="补充说明（可选，如：语气轻松一点）" />
      <div v-if="quickPrompts.length > 0" style="margin-top:8px;display:flex;gap:6px;flex-wrap:wrap">
        <el-button v-for="(q, i) in quickPrompts" :key="i" size="small" round @click="fillPrompt(q)">{{ q }}</el-button>
      </div>
      <div style="margin-top:10px">
        <el-button type="primary" icon="Promotion" :loading="running" @click="doRun">运行</el-button>
      </div>
      <div v-if="error" class="err-box">❌ {{ error }}</div>
    </el-card>

    <el-card v-if="result" shadow="never">
      <template #header><span style="font-weight:600">结果</span></template>
      <template v-if="outputParams.length > 0 && Object.keys(result.outputs || {}).length > 0">
        <el-descriptions :column="2" border size="small">
          <el-descriptions-item v-for="p in outputParams" :key="p.name" :label="p.label || p.name">
            {{ result.outputs[p.name] ?? '—' }}
          </el-descriptions-item>
        </el-descriptions>
      </template>
      <div style="margin-top:12px">
        <div style="font-weight:600;margin-bottom:4px;font-size:13px">原始回复</div>
        <pre class="reply-box">{{ result.reply }}</pre>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'

const route = useRoute()
const assistantId = Number(route.params.id) || 0
const assistant = ref<any>(null)
const inputParams = ref<any[]>([])
const outputParams = ref<any[]>([])
const quickPrompts = ref<string[]>([])
const inputs = ref<Record<string, any>>({})
const extraText = ref('')
const providers = ref<any[]>([])
const providerId = ref<number>(0)
const currentModel = ref('')
const running = ref(false)
const error = ref('')
const result = ref<any>(null)

const currentProvider = computed(() => providers.value.find((p: any) => p.id === providerId.value))
const modelOptions = computed(() => {
  const p = currentProvider.value
  const list = String(p?.models || '').split(',').map((s: string) => s.trim()).filter(Boolean)
  if (!list.includes(p?.model)) list.unshift(p?.model || '')
  return list.filter(Boolean)
})

onMounted(async () => {
  try {
    const res: any = await request.get('/ai/assistants')
    const list = res.data || []
    assistant.value = list.find((a: any) => a.id === assistantId) || null
    if (!assistant.value) { ElMessage.error('助手不存在'); return }
    document.title = assistant.value.assistantName
    try { inputParams.value = JSON.parse(assistant.value.inputParams || '[]') } catch { inputParams.value = [] }
    try { outputParams.value = JSON.parse(assistant.value.outputParams || '[]') } catch { outputParams.value = [] }
    try { quickPrompts.value = JSON.parse(assistant.value.quickPrompts || '[]') } catch { quickPrompts.value = [] }
    for (const p of inputParams.value) {
      inputs.value[p.name] = p.type === 'switch' ? (p.default === true || p.default === 'true') : (p.default || '')
    }
  } catch { /* 拦截器已提示 */ }

  try {
    const res: any = await request.get('/ai/providers/enabled')
    providers.value = res.data || []
    if (providers.value.length > 0) {
      providerId.value = providers.value[0].id
      currentModel.value = providers.value[0].model || ''
    }
  } catch { /* 未配置供应商 */ }
})

function onProviderChange() {
  currentModel.value = currentProvider.value?.model || ''
}

function selectOptions(p: any): string[] {
  return String(p.options || '').split(/[,，\n]/).map((s: string) => s.trim()).filter(Boolean)
}

/** 点击辅助提问词按钮 → 填入补充说明（已有内容时换行追加） */
function fillPrompt(q: string) {
  extraText.value = extraText.value.trim() ? extraText.value.trim() + '\n' + q : q
}

async function doRun() {
  if (!providerId.value) { ElMessage.warning('请先在系统设置 → 大模型设置中启用供应商'); return }
  running.value = true
  error.value = ''
  result.value = null
  try {
    const res: any = await request.post('/ai/assistant-run', {
      assistantId, providerId: providerId.value, model: currentModel.value || null,
      inputs: inputs.value, extraText: extraText.value
    })
    result.value = res.data
  } catch (e: any) {
    error.value = e?.message || '运行失败'
  } finally { running.value = false }
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:flex-start;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
.err-box { margin-top:10px;padding:8px 12px;background:#fef0f0;border:1px solid #fbc4c4;border-radius:6px;color:#f56c6c;font-size:12px;white-space:pre-wrap }
.reply-box { background:#f6f8fa;border:1px solid #e4e7ed;border-radius:6px;padding:10px;font-size:12px;line-height:1.7;white-space:pre-wrap;word-break:break-all;margin:0;max-height:400px;overflow:auto }
</style>
