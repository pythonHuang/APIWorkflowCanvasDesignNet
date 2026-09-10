<template>
  <div class="page-container">
    <div class="page-header">
      <h2>🤖 流程智能编排助手</h2>
      <div style="display:flex;gap:8px;align-items:center">
        <span style="font-size:12px;color:#888">模型:</span>
        <el-select v-model="providerId" size="small" style="width:150px" @change="onProviderChange">
          <el-option v-for="p in providers" :key="p.id" :label="p.providerName" :value="p.id" />
        </el-select>
        <el-select v-model="currentModel" size="small" style="width:170px" @change="saveModelPreference">
          <el-option v-for="m in modelOptions" :key="m" :label="m" :value="m" />
        </el-select>
        <el-button size="small" @click="$router.push('/system/ai-provider')">模型管理</el-button>
      </div>
    </div>

    <el-card style="margin-bottom:12px" shadow="never">
      <div style="display:flex;gap:8px;margin-bottom:8px">
        <el-input v-model="flowName" placeholder="流程名称（确认生成时必填）" size="small" style="width:220px" />
        <el-input v-model="groupName" placeholder="分组（可选）" size="small" style="width:160px" />
      </div>
      <el-input v-model="requirement" type="textarea" :rows="5"
        placeholder="描述编排需求，例如：接收用户id，先调用获取用户信息接口，再根据用户id查询该用户的订单列表，最后返回用户名称和订单列表" />
      <div style="margin-top:8px;display:flex;gap:8px;align-items:center">
        <el-button type="primary" icon="MagicStick" :loading="generating" @click="doGenerate">生成编排</el-button>
        <span style="font-size:12px;color:#909399">AI 会从平台已接入的接口中选择并编排，生成后请预览确认再入库。</span>
      </div>
      <div v-if="error" class="err-box">❌ {{ error }}</div>
    </el-card>

    <!-- 预览 -->
    <el-card v-if="preview" shadow="never">
      <template #header>
        <div style="display:flex;justify-content:space-between;align-items:center">
          <span style="font-weight:600">编排预览（{{ preview.nodes.length }} 个节点）</span>
          <el-button type="success" size="small" icon="Check" :loading="applying" @click="doApply">确认生成</el-button>
        </div>
      </template>
      <el-table :data="preview.nodes" size="small" border max-height="400">
        <el-table-column label="#" width="50" type="index" />
        <el-table-column prop="elementType" label="类型" width="110">
          <template #default="{ row }">
            <el-tag size="small">{{ nodeTypeName(row.elementType) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="label" label="名称" min-width="160" show-overflow-tooltip />
        <el-table-column label="连线 →" min-width="200">
          <template #default="{ row }">
            <span style="font-size:12px;color:#666">{{ outgoingsLabel(row) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="详情" min-width="220" show-overflow-tooltip>
          <template #default="{ row }">{{ nodeDetail(row) }}</template>
        </el-table-column>
      </el-table>

      <!-- 流程入参/出参 -->
      <div style="display:flex;gap:16px;margin-top:12px">
        <div style="flex:1">
          <div style="font-weight:600;margin:0 0 6px;font-size:13px">流程入参（{{ preview.inputParams?.length || 0 }}）</div>
          <el-table :data="preview.inputParams || []" size="small" border>
            <el-table-column prop="paramCode" label="参数code" min-width="130" show-overflow-tooltip />
            <el-table-column prop="paramName" label="名称" min-width="100" show-overflow-tooltip />
            <el-table-column prop="paramType" label="类型" width="90" />
            <el-table-column label="必填" width="60" align="center">
              <template #default="{ row }"><el-tag size="small" :type="row.required ? 'danger' : 'info'">{{ row.required ? '是' : '否' }}</el-tag></template>
            </el-table-column>
            <el-table-column prop="description" label="说明" min-width="120" show-overflow-tooltip />
          </el-table>
        </div>
        <div style="flex:1">
          <div style="font-weight:600;margin:0 0 6px;font-size:13px">流程出参（{{ preview.outputParams?.length || 0 }}）</div>
          <el-table :data="preview.outputParams || []" size="small" border>
            <el-table-column prop="paramCode" label="参数code" min-width="130" show-overflow-tooltip />
            <el-table-column prop="paramName" label="名称" min-width="100" show-overflow-tooltip />
            <el-table-column prop="paramType" label="类型" width="90" />
            <el-table-column prop="description" label="说明" min-width="120" show-overflow-tooltip />
          </el-table>
        </div>
      </div>

      <div v-if="applied" style="margin-top:10px;color:#67c23a;font-size:13px">
        ✅ 流程已创建：{{ applied.flowName }}（key: {{ applied.flowKey }}）
        <el-button size="small" link type="primary" @click="openDesigner(applied)">去设计器查看</el-button>
      </div>
    </el-card>

    <el-card v-else shadow="never">
      <el-empty description="输入需求后点击「生成编排」，AI 将自动选接口、映射参数、连线并生成条件分支" :image-size="80" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'

const router = useRouter()
const providers = ref<any[]>([])
const providerId = ref<number>(0)
const currentModel = ref('')
const requirement = ref('')
const flowName = ref('')
const groupName = ref('')
const generating = ref(false)
const applying = ref(false)
const error = ref('')
const preview = ref<any>(null)
const applied = ref<any>(null)

const currentProvider = computed(() => providers.value.find((p: any) => p.id === providerId.value))
const modelOptions = computed(() => {
  const p = currentProvider.value
  const list = String(p?.models || '').split(',').map((s: string) => s.trim()).filter(Boolean)
  if (!list.includes(p?.model)) list.unshift(p?.model || '')
  return list.filter(Boolean)
})

onMounted(async () => {
  try {
    const res: any = await request.get('/ai/providers/enabled')
    providers.value = res.data || []
    if (providers.value.length > 0) {
      providerId.value = providers.value[0].id
      currentModel.value = currentProvider.value?.model || ''
    }
  } catch { /* 未配置供应商 */ }
})

function onProviderChange() {
  currentModel.value = currentProvider.value?.model || ''
}

function saveModelPreference() { /* 切换模型即时生效 */ }

async function doGenerate() {
  if (!requirement.value.trim()) { ElMessage.warning('请先描述编排需求'); return }
  if (!providerId.value) { ElMessage.warning('请先在系统设置 → 大模型设置中启用供应商'); return }
  generating.value = true
  error.value = ''
  preview.value = null
  applied.value = null
  try {
    const suitesRes: any = await request.get('/suite/list')
    const suites = suitesRes.data || []
    const apis: any[] = []
    for (const suite of suites) {
      const apisRes: any = await request.post('/suite/api/list', { suiteCode: suite.suiteCode })
      for (const a of (apisRes.data || [])) {
        apis.push({
          suiteCode: suite.suiteCode,
          methodCode: a.methodCode,
          methodName: a.methodName,
          methodDesc: a.methodDesc,
          url: a.url,
          method: a.requestType || a.method || 'POST'
        })
      }
    }
    const res: any = await request.post('/ai/generate-flow', {
      requirement: requirement.value,
      providerId: providerId.value,
      model: currentModel.value || null,
      apis,
      inputParams: [],
      outputParams: []
    })
    preview.value = res.data
  } catch (e: any) {
    error.value = e?.message || '生成失败'
  } finally { generating.value = false }
}

async function doApply() {
  if (!flowName.value.trim()) { ElMessage.warning('请先填写流程名称'); return }
  applying.value = true
  try {
    const res: any = await request.post('/ai/apply-flow', {
      flowName: flowName.value, flowDesc: requirement.value.slice(0, 200), groupName: groupName.value,
      nodes: preview.value.nodes,
      inputParams: preview.value.inputParams || [],
      outputParams: preview.value.outputParams || []
    })
    applied.value = res.data
    ElMessage.success('流程已生成')
  } catch (e: any) {
    error.value = e?.message || '生成失败'
  } finally { applying.value = false }
}

function openDesigner(item: any) {
  router.push(`/flow/design?flowKey=${item.flowKey}`)
}

const typeNames: Record<string, string> = {
  START: '开始', END: '结束', METHOD: '接口', CONDITION: '条件', MERGE: '汇聚', ASSIGN: '赋值',
  CODE: '代码', MYSQL: '数据库', SUB_FLOW: '子流程', LOOP: '循环', DELAY: '延迟', PARALLEL: '并行',
  NOTIFY: '通知', TRANSFORM: '模板转换'
}
function nodeTypeName(t: string) { return typeNames[t] || t }

function outgoingsLabel(row: any): string {
  const targets = row.outgoings || []
  if (!targets.length) return '—'
  const label = (key: string) => preview.value?.nodes?.find((n: any) => n.key === key)?.label || key
  return targets.map(label).join('、')
}

function nodeDetail(row: any): string {
  if (row.elementType === 'METHOD') return `${row.method?.method} ${row.method?.url || ''}`
  if (row.elementType === 'CONDITION') return (row.conditions || []).map((c: any) => c.conditionName + (c.conditionType === 'CUSTOM' ? `: ${c.expression}` : '')).join(' | ')
  if (row.elementType === 'MYSQL') return row.mysqlConfig?.sql || ''
  return ''
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
.err-box { margin-top:10px;padding:8px 12px;background:#fef0f0;border:1px solid #fbc4c4;border-radius:6px;color:#f56c6c;font-size:12px;white-space:pre-wrap }
</style>
