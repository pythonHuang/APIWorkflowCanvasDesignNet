<template>
  <div class="page-container">
    <div class="page-header">
      <h2>🤖 模型助手管理</h2>
      <el-button type="primary" size="small" @click="openAdd">添加助手</el-button>
    </div>
    <el-card>
      <el-table :data="tableData" v-loading="loading">
        <el-table-column prop="assistantName" label="助手名称" width="180" />
        <el-table-column prop="description" label="描述" min-width="220" show-overflow-tooltip />
        <el-table-column label="输入参数" width="140">
          <template #default="{ row }">{{ countParams(row.inputParams) }} 个</template>
        </el-table-column>
        <el-table-column label="输出参数" width="140">
          <template #default="{ row }">{{ countParams(row.outputParams) }} 个</template>
        </el-table-column>
        <el-table-column label="状态" width="90" align="center">
          <template #default="{ row }">
            <el-switch :model-value="row.enabled === 1" @change="(v: boolean) => toggleEnabled(row, v)" size="small"
              active-text="启用" inactive-text="禁用" inline-prompt style="--el-switch-on-color:#67c23a" />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="190">
          <template #default="{ row }">
            <el-button size="small" link type="primary" @click="$router.push(`/ai/assistant/${row.id}`)">运行</el-button>
            <el-button size="small" link @click="openEdit(row)">编辑</el-button>
            <el-button size="small" type="info" link @click="openPublish(row)">发布</el-button>
            <el-button size="small" type="warning" link @click="openShare(row)">分享</el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div style="margin-top:10px;color:#909399;font-size:12px">
        每个助手配置名称、系统提示词、输入/输出参数列表；启用后自动出现在「模型助手」菜单中，运行页按输入参数表单填写并调用大模型，输出参数要求模型按 JSON 返回。
      </div>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="form.id ? '编辑助手' : '添加助手'" width="720px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="助手名称"><el-input v-model="form.assistantName" placeholder="如 周报生成助手" /></el-form-item>
        <el-form-item label="描述"><el-input v-model="form.description" placeholder="助手用途说明（菜单提示）" /></el-form-item>
        <el-form-item label="系统提示词">
          <div style="width:100%">
            <el-input v-model="form.systemPrompt" type="textarea" :rows="4"
              placeholder="如：你是一名专业的文案专家，根据用户输入生成简洁有力的文案。" />
            <div style="display:flex;justify-content:space-between;align-items:center;margin-top:4px">
              <span style="font-size:11px;color:#909399">可用大模型对草稿提示词进行结构化润色</span>
              <el-button size="small" icon="MagicStick" :loading="optimizing" @click="doOptimizePrompt">AI 优化</el-button>
            </div>
          </div>
        </el-form-item>
        <el-form-item label="图标">
          <div style="display:flex;gap:8px;align-items:center;width:100%">
            <div v-if="form.icon" class="assistant-icon-preview">
              <img v-if="isImageIcon(form.icon)" :src="form.icon" style="width:28px;height:28px;object-fit:contain" />
              <span v-else style="font-size:22px">{{ form.icon }}</span>
            </div>
            <el-input v-model="form.icon" placeholder="emoji 图标（如 📝）或上传图片" size="small" style="flex:1" clearable />
            <el-upload :show-file-list="false" :auto-upload="false" accept="image/*" :on-change="onIconUpload" style="display:inline-block">
              <el-button size="small">上传</el-button>
            </el-upload>
            <el-button size="small" icon="MagicStick" :loading="iconGenerating" @click="doGenerateIcon">AI 生成</el-button>
          </div>
          <div style="font-size:11px;color:#909399;margin-top:4px">图标显示在「模型助手」菜单与运行页标题左侧。</div>
        </el-form-item>
        <el-form-item label="输入参数">
          <div style="width:100%">
            <div v-for="(p, i) in inputParams" :key="i" style="display:flex;gap:4px;margin-bottom:4px">
              <el-input v-model="p.name" placeholder="参数名" size="small" style="width:130px;flex-shrink:0" />
              <el-input v-model="p.label" placeholder="显示名" size="small" style="width:110px;flex-shrink:0" />
              <el-select v-model="p.type" size="small" style="width:90px;flex-shrink:0">
                <el-option value="text" label="文本" />
                <el-option value="number" label="数字" />
                <el-option value="date" label="日期" />
                <el-option value="switch" label="开关" />
                <el-option value="select" label="下拉" />
              </el-select>
              <el-input v-if="p.type==='select'" v-model="p.options" placeholder="选项,逗号分隔" size="small" style="width:170px;flex-shrink:0" />
              <el-input v-else-if="p.type!=='switch'" v-model="p.default" placeholder="默认值" size="small" style="width:100px;flex-shrink:0" />
              <el-button size="small" type="danger" link @click="inputParams.splice(i,1)">删</el-button>
            </div>
            <el-button size="small" @click="inputParams.push({name:'',label:'',type:'text',default:''})">+添加输入参数</el-button>
          </div>
        </el-form-item>
        <el-form-item label="输出参数">
          <div style="width:100%">
            <div v-for="(p, i) in outputParams" :key="i" style="display:flex;gap:4px;margin-bottom:4px">
              <el-input v-model="p.name" placeholder="参数名" size="small" style="width:150px;flex-shrink:0" />
              <el-input v-model="p.label" placeholder="显示名" size="small" style="width:150px;flex-shrink:0" />
              <el-button size="small" type="danger" link @click="outputParams.splice(i,1)">删</el-button>
            </div>
            <el-button size="small" @click="outputParams.push({name:'',label:''})">+添加输出参数</el-button>
            <div style="font-size:11px;color:#909399;margin-top:4px">配置输出参数后，运行时会要求模型按 JSON 返回并在结果区按参数展示。</div>
          </div>
        </el-form-item>
        <el-form-item label="辅助提问词">
          <div style="width:100%">
            <div v-for="(_q, i) in quickPrompts" :key="i" style="display:flex;gap:4px;margin-bottom:4px">
              <el-input v-model="quickPrompts[i]" :placeholder="i === 0 ? '如 帮我写一篇本周工作总结' : '提问词'" size="small" style="flex:1" />
              <el-button size="small" type="danger" link @click="quickPrompts.splice(i,1)">删</el-button>
            </div>
            <el-button size="small" @click="quickPrompts.push('')">+添加提问词</el-button>
            <div style="font-size:11px;color:#909399;margin-top:4px">运行页中显示为快捷按钮，点击即填入补充说明，方便常用提问一键发起。</div>
          </div>
        </el-form-item>
        <el-form-item label="模型参数">
          <div style="width:100%">
            <div style="display:flex;gap:8px;align-items:center;margin-bottom:6px">
              <span style="width:64px;font-size:12px;color:#666;flex-shrink:0">默认模型</span>
              <el-select v-model="form.providerId" size="small" style="flex:1" clearable placeholder="供应商(空=第一个启用)" @change="onAssistantProviderChange">
                <el-option v-for="p in aiProviders" :key="p.id" :label="p.providerName" :value="p.id" />
              </el-select>
              <el-select v-model="form.model" size="small" style="flex:1" clearable filterable allow-create default-first-option placeholder="模型(空=供应商默认)">
                <el-option v-for="m in assistantModelOptions" :key="m" :value="m">
                  <span v-if="modelKindEmoji(m)" style="margin-right:4px">{{ modelKindEmoji(m) }}</span>{{ m }}
                </el-option>
              </el-select>
            </div>
            <div style="display:flex;align-items:center;gap:8px;margin-bottom:6px">
              <span style="width:64px;font-size:12px;color:#666;flex-shrink:0">深度思考</span>
              <el-switch v-model="form.enableThinking" size="small" />
              <span style="font-size:11px;color:#909399">推理模型（deepseek-reasoner / Qwen 思考模式）生效</span>
            </div>
            <div style="display:flex;align-items:center;gap:8px;margin-bottom:6px">
              <span style="width:64px;font-size:12px;color:#666;flex-shrink:0">温度</span>
              <el-slider v-model="form.temperature" size="small" style="flex:1" :min="0" :max="2" :step="0.1" :format-tooltip="tempFormat" />
              <span style="width:36px;font-size:12px;color:#666;text-align:right;flex-shrink:0">{{ (form.temperature || 0).toFixed(1) }}</span>
            </div>
            <div style="display:flex;align-items:center;gap:8px;margin-bottom:6px">
              <span style="width:64px;font-size:12px;color:#666;flex-shrink:0">最大输出</span>
              <el-input-number v-model="form.maxTokens" size="small" style="flex:1" :min="0" :step="100" controls-position="right" />
              <span style="font-size:11px;color:#909399;flex-shrink:0">0=不限制</span>
            </div>
            <div style="display:flex;align-items:center;gap:8px">
              <span style="width:64px;font-size:12px;color:#666;flex-shrink:0">随机种子</span>
              <el-input-number v-model="form.seed" size="small" style="flex:1" :min="0" :step="1" controls-position="right" />
              <span style="font-size:11px;color:#909399;flex-shrink:0">0=随机</span>
            </div>
          </div>
        </el-form-item>
        <el-form-item label="支持的能力">
          <div style="width:100%">
            <div style="display:flex;gap:8px;align-items:center;margin-bottom:6px">
              <span style="width:40px;font-size:12px;color:#666">Skills</span>
              <el-select v-model="capSkills" size="small" style="flex:1" multiple collapse-tags collapse-tags-tooltip clearable placeholder="勾选该助手支持的技能">
                <el-option v-for="s in skillOptions" :key="s.id" :label="`${s.skillName}（${s.groupName}）`" :value="s.id" />
              </el-select>
            </div>
            <div style="display:flex;gap:8px;align-items:center;margin-bottom:6px">
              <span style="width:40px;font-size:12px;color:#666">接口</span>
              <el-select v-model="capApis" size="small" style="flex:1" multiple collapse-tags collapse-tags-tooltip filterable clearable placeholder="勾选该助手可调用的接口">
                <el-option v-for="a in apiOptions" :key="a.methodCode" :label="`${a.methodName}（${a.methodCode}）`" :value="a.methodCode" />
              </el-select>
            </div>
            <div style="display:flex;gap:8px;align-items:center;margin-bottom:6px">
              <span style="width:40px;font-size:12px;color:#666">流程</span>
              <el-select v-model="capFlows" size="small" style="flex:1" multiple collapse-tags collapse-tags-tooltip filterable clearable placeholder="勾选该助手可调用的流程">
                <el-option v-for="f in flowOptions" :key="f.flowKey" :label="`${f.flowName}（${f.flowKey}）`" :value="f.flowKey" />
              </el-select>
            </div>
            <div style="display:flex;gap:8px;align-items:center">
              <span style="width:40px;font-size:12px;color:#666">工具</span>
              <el-input v-model="capTools" size="small" style="flex:1" placeholder="逗号分隔的工具名（如 web_search, calculator）" />
            </div>
            <div style="font-size:11px;color:#909399;margin-top:4px">配置该助手支持的能力范围（Skills/接口/流程/工具）。</div>
          </div>
        </el-form-item>
        <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible=false">取消</el-button>
        <el-button type="primary" @click="doSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 发布到市场 -->
    <MarketPublishDialog v-model:visible="publishVisible" item-type="assistant"
      :default-name="publishForm.itemName" :default-desc="publishForm.description" :default-group="publishForm.groupName"
      :content-json="publishForm.contentJson" />

    <!-- 分享到官方市场（GitHub PR） -->
    <MarketShareDialog v-model:visible="shareVisible" item-type="assistant"
      :item-name="shareForm.itemName" :description="shareForm.description" :icon="shareForm.icon"
      :content-json="shareForm.contentJson" />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import request from '../../utils/request'
import MarketPublishDialog from '../../components/MarketPublishDialog.vue'
import MarketShareDialog from '../../components/MarketShareDialog.vue'
import { modelKindEmoji } from '../../utils/aiModel'

// 发布到市场
const publishVisible = ref(false)
const publishForm = ref<any>({ itemName: '', description: '', groupName: '', contentJson: '{}' })

/** 组装助手内容 JSON */
function buildAssistantContentJson(row: any) {
  return JSON.stringify({
    assistantName: row.assistantName, description: row.description, systemPrompt: row.systemPrompt,
    inputParams: row.inputParams, outputParams: row.outputParams, quickPrompts: row.quickPrompts, icon: row.icon
  })
}

function openPublish(row: any) {
  publishForm.value = {
    itemName: row.assistantName || '',
    description: row.description || '',
    groupName: '',
    contentJson: buildAssistantContentJson(row)
  }
  publishVisible.value = true
}

// 分享到官方市场（GitHub PR）
const shareVisible = ref(false)
const shareForm = ref<any>({ itemName: '', description: '', icon: '', contentJson: '{}' })
function openShare(row: any) {
  shareForm.value = {
    itemName: row.assistantName || '',
    description: row.description || '',
    icon: row.icon || '🤖',
    contentJson: buildAssistantContentJson(row)
  }
  shareVisible.value = true
}

const loading = ref(false)
const tableData = ref<any[]>([])
const dialogVisible = ref(false)
const form = ref<any>({ id: 0, assistantName: '', description: '', systemPrompt: '', enabled: true })
const inputParams = ref<any[]>([])
const outputParams = ref<any[]>([])
const quickPrompts = ref<string[]>([])
const optimizing = ref(false)
const iconGenerating = ref(false)
// 支持的能力（skills/apis/flows/tools）
const capSkills = ref<number[]>([])
const capApis = ref<string[]>([])
const capFlows = ref<string[]>([])
const capTools = ref('')
const skillOptions = ref<any[]>([])
const apiOptions = ref<any[]>([])
const flowOptions = ref<any[]>([])

// 模型参数（默认供应商/模型/深度思考/温度/最大输出/随机种子）
const aiProviders = ref<any[]>([])
const assistantModelOptions = computed(() => {
  const p = aiProviders.value.find((x: any) => x.id === form.value.providerId)
  const list = String(p?.models || '').split(',').map((s: string) => s.trim()).filter(Boolean)
  if (p?.model && !list.includes(p.model)) list.unshift(p.model)
  return list.filter(Boolean)
})
function onAssistantProviderChange() {
  const p = aiProviders.value.find((x: any) => x.id === form.value.providerId)
  if (p) form.value.model = p.model || ''
}
function tempFormat(v: number) { return v.toFixed(1) }

/** 加载启用的供应商（默认模型选择用） */
async function loadAiProviders() {
  try {
    const res: any = await request.get('/ai/providers/enabled')
    aiProviders.value = res.data || []
  } catch { /* 未配置供应商 */ }
}

/** 加载能力选项（技能/接口/流程） */
async function loadCapabilityOptions() {
  try {
    const [skillRes, suiteRes, flowRes]: any[] = await Promise.all([
      request.get('/skill/list', { params: { enabled: 1 } }),
      request.get('/suite/list'),
      request.post('/flow/definition/page', { pageNum: 1, pageSize: 500 })
    ])
    skillOptions.value = skillRes.data || []
    flowOptions.value = flowRes.data?.list || flowRes.data?.records || []
    const suites = suiteRes.data || []
    apiOptions.value = []
    for (const s of suites) {
      try {
        const apisRes: any = await request.post('/suite/api/list', { suiteCode: s.suiteCode })
        for (const a of (apisRes.data || [])) {
          apiOptions.value.push({ methodCode: a.methodCode, methodName: a.methodName })
        }
      } catch { /* 忽略单个套件失败 */ }
    }
  } catch { /* 忽略 */ }
}

/** 解析能力 JSON 到表单状态 */
function parseCapabilities(json: string) {
  try {
    const c = JSON.parse(json || '{}')
    capSkills.value = c.skills || []
    capApis.value = c.apis || []
    capFlows.value = c.flows || []
    capTools.value = (c.tools || []).join(',')
  } catch {
    capSkills.value = []; capApis.value = []; capFlows.value = []; capTools.value = ''
  }
}

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.get('/ai/assistants')
    tableData.value = res.data || []
  } finally { loading.value = false }
}

function countParams(json: string): number {
  try { return JSON.parse(json || '[]').length } catch { return 0 }
}

function openAdd() {
  form.value = {
    id: 0, assistantName: '', description: '', systemPrompt: '', icon: '', enabled: true,
    providerId: 0, model: '', enableThinking: false, temperature: 0.7, maxTokens: 0, seed: 0
  }
  inputParams.value = []
  outputParams.value = []
  quickPrompts.value = []
  parseCapabilities('')
  dialogVisible.value = true
  loadCapabilityOptions()
  loadAiProviders()
}
function openEdit(row: any) {
  form.value = {
    ...row,
    enabled: row.enabled === 1,
    enableThinking: row.enableThinking === 1,
    temperature: row.temperature ?? 0.7,
    maxTokens: row.maxTokens || 0,
    seed: row.seed || 0,
    providerId: row.providerId || 0,
    model: row.model || ''
  }
  try { inputParams.value = JSON.parse(row.inputParams || '[]') } catch { inputParams.value = [] }
  try { outputParams.value = JSON.parse(row.outputParams || '[]') } catch { outputParams.value = [] }
  try { quickPrompts.value = JSON.parse(row.quickPrompts || '[]') } catch { quickPrompts.value = [] }
  parseCapabilities(row.capabilities || '')
  dialogVisible.value = true
  loadCapabilityOptions()
  loadAiProviders()
}

function isImageIcon(icon: string): boolean {
  if (!icon) return false
  if (typeof icon !== 'string') return false
  return icon.startsWith('data:image') || icon.startsWith('http')
}

/** 上传图标：转 base64 data URL */
function onIconUpload(file: any) {
  const raw = file?.raw
  if (!raw) return
  if (raw.size > 200 * 1024) { ElMessage.warning('图片不能超过 200KB'); return }
  const reader = new FileReader()
  reader.onload = () => { form.value.icon = reader.result as string }
  reader.readAsDataURL(raw)
}

/** AI 优化系统提示词 */
async function doOptimizePrompt() {
  if (!form.value.systemPrompt?.trim()) { ElMessage.warning('请先填写系统提示词草稿'); return }
  optimizing.value = true
  try {
    const res: any = await request.post('/ai/optimize-prompt', { prompt: form.value.systemPrompt })
    form.value.systemPrompt = res.data?.prompt || form.value.systemPrompt
    ElMessage.success('提示词已优化')
  } catch (e: any) {
    ElMessage.error(e?.message || '优化失败，请确认已配置启用的大模型')
  } finally { optimizing.value = false }
}

/** AI 生成 emoji 图标 */
async function doGenerateIcon() {
  if (!form.value.assistantName?.trim()) { ElMessage.warning('请先填写助手名称'); return }
  iconGenerating.value = true
  try {
    const res: any = await request.post('/ai/generate-icon', { name: form.value.assistantName, description: form.value.description })
    if (res.data?.icon) form.value.icon = res.data.icon
  } catch (e: any) {
    ElMessage.error(e?.message || '生成失败，请确认已配置启用的大模型')
  } finally { iconGenerating.value = false }
}
async function doSave() {
  await request.post('/ai/assistant/save', {
    ...form.value,
    inputParams: JSON.stringify(inputParams.value),
    outputParams: JSON.stringify(outputParams.value),
    quickPrompts: JSON.stringify(quickPrompts.value.filter((q: string) => q.trim())),
    capabilities: JSON.stringify({
      skills: capSkills.value,
      apis: capApis.value,
      flows: capFlows.value,
      tools: capTools.value.split(',').map((s: string) => s.trim()).filter(Boolean)
    })
  })
  ElMessage.success('保存成功')
  dialogVisible.value = false
  loadData()
}
async function toggleEnabled(row: any, v: boolean) {
  await request.post('/ai/assistant/toggle', { id: row.id, enabled: v })
  row.enabled = v ? 1 : 0
  ElMessage.success(v ? '已启用' : '已禁用')
}
async function doDelete(row: any) {
  await ElMessageBox.confirm(`确认删除助手「${row.assistantName}」？`, '提示', { type: 'warning' })
  await request.delete(`/ai/assistant/${row.id}`)
  ElMessage.success('已删除')
  loadData()
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
.assistant-icon-preview { width:32px;height:32px;border:1px solid #e4e7ed;border-radius:6px;display:flex;align-items:center;justify-content:center;flex-shrink:0;overflow:hidden }
</style>
