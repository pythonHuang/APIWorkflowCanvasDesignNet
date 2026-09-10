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
        <el-button size="small" @click="startNew">新对话</el-button>
        <el-button size="small" @click="loadHistory">历史</el-button>
      </div>
    </div>

    <!-- 输入参数（未开始对话时填写） -->
    <el-card v-if="!conversation" style="margin-bottom:12px" shadow="never">
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
      <div style="margin-top:10px">
        <el-button type="primary" icon="ChatDotRound" :loading="starting" @click="startConversation">开始对话</el-button>
      </div>
    </el-card>

    <!-- 对话区 -->
    <el-card v-if="conversation" shadow="never" style="flex:1;display:flex;flex-direction:column;min-height:0">
      <div class="chat-box" ref="chatBoxRef">
        <div v-for="(m, i) in messages" :key="i" :class="['chat-msg', m.role]">
          <div class="chat-bubble">{{ m.content }}</div>
        </div>
        <div v-if="chatLoading" class="chat-msg assistant">
          <div class="chat-bubble"><el-icon class="is-loading"><Loading /></el-icon> 思考中...</div>
        </div>
        <div v-if="!messages.length && !chatLoading" class="chat-msg assistant">
          <div class="chat-bubble">你好，请开始提问（可点击下方辅助提问词快速发起）。</div>
        </div>
      </div>
      <div v-if="quickPrompts.length > 0" style="margin-top:8px;display:flex;gap:6px;flex-wrap:wrap">
        <el-button v-for="(q, i) in quickPrompts" :key="i" size="small" round @click="fillPrompt(q)">{{ q }}</el-button>
      </div>
      <div style="display:flex;gap:8px;margin-top:10px">
        <el-input v-model="draft" :disabled="conversation?.status === 1" placeholder="输入消息，回车发送" size="small"
          style="flex:1" @keydown.enter="sendMessage" />
        <el-button size="small" type="primary" icon="Promotion" :disabled="conversation?.status === 1" :loading="chatLoading" @click="sendMessage">发送</el-button>
        <el-button size="small" type="warning" :disabled="conversation?.status === 1" :loading="ending" @click="endConversation">结束对话</el-button>
      </div>
      <div v-if="error" class="err-box">❌ {{ error }}</div>
    </el-card>

    <!-- 最终结果（结束对话后） -->
    <el-card v-if="conversation?.status === 1 && outputParams.length > 0" shadow="never" style="margin-top:12px">
      <template #header><span style="font-weight:600">✅ 最终结果（已结束对话）</span></template>
      <el-descriptions :column="2" border size="small">
        <el-descriptions-item v-for="p in outputParams" :key="p.name" :label="p.label || p.name">
          {{ finalOutputs[p.name] ?? '—' }}
        </el-descriptions-item>
      </el-descriptions>
    </el-card>

    <!-- 历史抽屉 -->
    <el-drawer v-model="historyVisible" title="对话历史" size="420px">
      <div v-for="c in historyList" :key="c.id" class="history-item" @click="openConversation(c.id)">
        <div style="flex:1;min-width:0">
          <div style="font-size:13px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">{{ c.title || '(无标题)' }}</div>
          <div style="font-size:11px;color:#999">{{ c.model || '默认模型' }} · {{ c.createdAt?.slice(0, 16) }}</div>
        </div>
        <el-tag size="small" :type="c.status === 1 ? 'success' : 'primary'">{{ c.status === 1 ? '已结束' : '进行中' }}</el-tag>
        <el-button size="small" type="danger" link @click.stop="deleteConversation(c.id)">删</el-button>
      </div>
      <el-empty v-if="!historyList.length" description="暂无历史对话" :image-size="60" />
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
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
const providers = ref<any[]>([])
const providerId = ref<number>(0)
const currentModel = ref('')

// 对话状态
const conversation = ref<any>(null)     // { id, status, ... }
const messages = ref<{ role: string; content: string }[]>([])
const draft = ref('')
const chatLoading = ref(false)
const starting = ref(false)
const ending = ref(false)
const error = ref('')
const finalOutputs = ref<Record<string, any>>({})
const chatBoxRef = ref<HTMLElement | null>(null)
const historyVisible = ref(false)
const historyList = ref<any[]>([])

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
    assistant.value = (res.data || []).find((a: any) => a.id === assistantId) || null
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

function fillPrompt(q: string) {
  if (conversation.value) { draft.value = q; return }
  // 未开始对话时作为首条消息暂存
  draft.value = draft.value.trim() ? draft.value.trim() + '\n' + q : q
}

async function startConversation() {
  if (!providerId.value) { ElMessage.warning('请先在系统设置 → 大模型设置中启用供应商'); return }
  starting.value = true
  error.value = ''
  try {
    const res: any = await request.post('/ai/conversation/start', {
      assistantId, providerId: providerId.value, model: currentModel.value || null, inputs: inputs.value
    })
    conversation.value = { id: res.data.id, status: 0 }
    messages.value = []
    finalOutputs.value = {}
    // 有暂存提问词时直接发送首条消息
    if (draft.value.trim()) {
      const first = draft.value
      draft.value = ''
      await sendMessage(first)
    }
  } catch (e: any) {
    error.value = e?.message || '开始对话失败'
  } finally { starting.value = false }
}

async function sendMessage(text?: string) {
  const content = (text ?? draft.value).trim()
  if (!content || !conversation.value || conversation.value.status === 1) return
  draft.value = ''
  messages.value.push({ role: 'user', content })
  chatLoading.value = true
  error.value = ''
  try {
    const res: any = await request.post('/ai/conversation/chat', {
      conversationId: conversation.value.id, content,
      providerId: providerId.value, model: currentModel.value || null
    })
    messages.value.push({ role: 'assistant', content: res.data?.reply || '' })
  } catch (e: any) {
    error.value = e?.message || '发送失败'
  } finally {
    chatLoading.value = false
    scrollToBottom()
  }
}

async function endConversation() {
  if (!conversation.value || conversation.value.status === 1) return
  ending.value = true
  error.value = ''
  try {
    const res: any = await request.post('/ai/conversation/end', {
      conversationId: conversation.value.id, providerId: providerId.value, model: currentModel.value || null
    })
    finalOutputs.value = res.data?.outputs || {}
    conversation.value.status = 1
    ElMessage.success('对话已结束')
  } catch (e: any) {
    error.value = e?.message || '结束失败'
  } finally { ending.value = false }
}

function startNew() {
  conversation.value = null
  messages.value = []
  draft.value = ''
  error.value = ''
  finalOutputs.value = {}
}

async function loadHistory() {
  historyVisible.value = true
  try {
    const res: any = await request.get(`/ai/conversations/${assistantId}`)
    historyList.value = res.data || []
  } catch { historyList.value = [] }
}

async function openConversation(id: number) {
  try {
    const res: any = await request.get(`/ai/conversation/${id}`)
    const conv = res.data
    conversation.value = { id: conv.id, status: conv.status }
    try { messages.value = JSON.parse(conv.messages || '[]') } catch { messages.value = [] }
    try { finalOutputs.value = JSON.parse(conv.outputs || '{}') } catch { finalOutputs.value = {} }
    historyVisible.value = false
    await nextTick()
    scrollToBottom()
  } catch { /* 拦截器已提示 */ }
}

async function deleteConversation(id: number) {
  await request.delete(`/ai/conversation/${id}`)
  ElMessage.success('已删除')
  loadHistory()
}

function scrollToBottom() {
  nextTick(() => {
    if (chatBoxRef.value) chatBoxRef.value.scrollTop = chatBoxRef.value.scrollHeight
  })
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:flex-start;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
.err-box { margin-top:10px;padding:8px 12px;background:#fef0f0;border:1px solid #fbc4c4;border-radius:6px;color:#f56c6c;font-size:12px;white-space:pre-wrap }
.chat-box { flex:1;overflow-y:auto;min-height:200px;max-height:52vh;padding:4px }
.chat-msg { display:flex;margin-bottom:10px }
.chat-msg.user { justify-content:flex-end }
.chat-bubble { max-width:75%;padding:8px 12px;border-radius:10px;font-size:13px;line-height:1.7;white-space:pre-wrap;word-break:break-all;background:#f0f2f5 }
.chat-msg.user .chat-bubble { background:#409eff;color:#fff }
.history-item { display:flex;align-items:center;gap:8px;padding:10px 8px;border-bottom:1px solid #f0f2f5;cursor:pointer }
.history-item:hover { background:#f5f7fa }
</style>
