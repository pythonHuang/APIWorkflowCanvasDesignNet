<template>
  <div class="page-container">
    <div class="page-header">
      <h2>📊 报表智能助手</h2>
      <div style="display:flex;gap:8px;align-items:center">
        <span style="font-size:12px;color:#888">模型:</span>
        <el-select v-model="providerId" size="small" style="width:150px" @change="onProviderChange">
          <el-option v-for="p in providers" :key="p.id" :label="p.providerName" :value="p.id" />
        </el-select>
        <el-select v-model="currentModel" size="small" style="width:170px" filterable allow-create default-first-option>
          <el-option v-for="m in modelOptions" :key="m" :label="m" :value="m" />
        </el-select>
        <el-button size="small" @click="$router.push('/system/ai-provider')">模型管理</el-button>
      </div>
    </div>

    <el-card style="margin-bottom:12px" shadow="never">
      <div style="display:flex;gap:8px;margin-bottom:8px">
        <el-input v-model="reportName" placeholder="报表名称（确认生成时必填）" size="small" style="width:220px" />
        <el-input v-model="groupName" placeholder="分组（可选）" size="small" style="width:160px" />
      </div>
      <el-input v-model="requirement" type="textarea" :rows="4"
        placeholder="描述报表需求，例如：生成月度销售统计报表，按日期范围筛选，按销售员汇总订单金额和数量，需要合计行" />
      <div style="margin-top:8px;display:flex;gap:8px;align-items:center">
        <el-button type="primary" icon="MagicStick" :loading="generating" @click="doGenerate">生成报表</el-button>
        <span style="font-size:12px;color:#909399">AI 会尽量复用已有数据视图/数据源/流程/接口生成数据集，自动设计查询参数与排版（汇总用公式/聚合），生成后请预览确认再入库。</span>
      </div>
      <div v-if="error" class="err-box">❌ {{ error }}</div>
    </el-card>

    <!-- 预览 -->
    <template v-if="preview">
      <el-card style="margin-bottom:12px" shadow="never">
        <template #header>
          <div style="display:flex;justify-content:space-between;align-items:center">
            <span style="font-weight:600">
              生成结果：{{ preview.datasets.length }} 个数据集 · {{ preview.rows }} 行 · {{ preview.cols }} 列 · {{ preview.paramsList.length }} 个查询参数
            </span>
            <el-button type="success" size="small" icon="Check" :loading="applying" @click="doApply">确认生成报表</el-button>
          </div>
        </template>
        <div style="display:flex;gap:12px">
          <div style="flex:1">
            <div style="font-weight:600;margin-bottom:4px;font-size:12px">数据集</div>
            <el-table :data="preview.datasets" size="small" border>
              <el-table-column prop="name" label="名称" min-width="120" show-overflow-tooltip />
              <el-table-column prop="sourceType" label="类型" width="90">
                <template #default="{ row }">{{ sourceLabel(row.sourceType) }}</template>
              </el-table-column>
              <el-table-column prop="sourceRef" label="引用" width="120" show-overflow-tooltip />
              <el-table-column prop="customSql" label="自定义SQL" min-width="160" show-overflow-tooltip />
            </el-table>
          </div>
          <div style="flex:1">
            <div style="font-weight:600;margin-bottom:4px;font-size:12px">查询参数</div>
            <el-table :data="preview.paramsList" size="small" border>
              <el-table-column prop="name" label="参数名" min-width="100" show-overflow-tooltip />
              <el-table-column prop="label" label="显示名" min-width="90" show-overflow-tooltip />
              <el-table-column prop="type" label="类型" width="80" />
              <el-table-column prop="options" label="选项" min-width="100" show-overflow-tooltip />
              <el-table-column prop="default" label="默认值" width="80" show-overflow-tooltip />
            </el-table>
          </div>
        </div>
      </el-card>

      <el-card shadow="never">
        <template #header><span style="font-weight:600">报表预览</span></template>
        <div v-loading="previewLoading" v-html="previewHtml" style="overflow:auto;max-height:60vh;border:1px solid #eee;padding:12px" />
        <div v-if="applied" style="margin-top:10px;color:#67c23a;font-size:13px">
          ✅ 报表已创建：{{ applied.name }}
          <el-button size="small" link type="primary" @click="$router.push(`/report/designer/${applied.id}`)">去设计器查看</el-button>
          <el-button size="small" link type="primary" @click="openReportView(applied.id)">打开正式访问页</el-button>
        </div>
      </el-card>
    </template>

    <el-card v-else shadow="never">
      <el-empty description="输入报表需求后点击「生成报表」，AI 自动生成数据集、查询参数与排版" :image-size="80" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'

const providers = ref<any[]>([])
const providerId = ref<number>(0)
const currentModel = ref('')
const reportName = ref('')
const groupName = ref('')
const requirement = ref('')
const generating = ref(false)
const applying = ref(false)
const previewLoading = ref(false)
const error = ref('')
const preview = ref<any>(null)
const previewHtml = ref('')
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
      currentModel.value = providers.value[0].model || ''
    }
  } catch { /* 未配置供应商 */ }
})

function onProviderChange() {
  currentModel.value = currentProvider.value?.model || ''
}

function sourceLabel(t: string) {
  return { dataview: '数据视图', sql: '自定义SQL', flow: '流程', api: '接口' }[t] || t
}

async function doGenerate() {
  if (!requirement.value.trim()) { ElMessage.warning('请先描述报表需求'); return }
  if (!providerId.value) { ElMessage.warning('请先在系统设置 → 大模型设置中启用供应商'); return }
  generating.value = true
  error.value = ''
  preview.value = null
  previewHtml.value = ''
  applied.value = null
  try {
    // 收集可复用上下文
    const dvRes: any = await request.post('/report/dataview/page', { pageNum: 1, pageSize: 200 })
    const dsRes: any = await request.get('/system/datasource/list')
    const flowRes: any = await request.get('/flow/info/list').catch(() => ({ data: [] }))
    const suiteRes: any = await request.get('/suite/list')
    const suites = suiteRes.data || []
    const apis: any[] = []
    for (const s of suites) {
      const apisRes: any = await request.post('/suite/api/list', { suiteCode: s.suiteCode })
      for (const a of (apisRes.data || [])) {
        apis.push({ methodCode: a.methodCode, methodName: a.methodName, suiteCode: s.suiteCode, url: a.url })
      }
    }
    const res: any = await request.post('/ai/generate-report', {
      requirement: requirement.value,
      reportName: reportName.value || null,
      providerId: providerId.value,
      model: currentModel.value || null,
      dataViews: (dvRes.data?.list || []).map((d: any) => ({ id: String(d.id), name: d.name, groupName: d.groupName, sql: d.sql })),
      dataSources: (dsRes.data || []).map((d: any) => ({ id: d.id, dataSourceName: d.dataSourceName, dataSourceType: d.dataSourceType })),
      flows: (flowRes.data || []).map((f: any) => ({ flowKey: f.flowKey, flowName: f.flowName })),
      apis
    })
    preview.value = res.data
    if (!reportName.value && res.data?.reportName) reportName.value = res.data.reportName
    // 渲染预览
    previewLoading.value = true
    try {
      const pv: any = await request.post('/report/preview-layout', { layoutJson: res.data.layoutJson, params: {} })
      previewHtml.value = pv.data?.html || ''
    } finally { previewLoading.value = false }
  } catch (e: any) {
    error.value = e?.message || '生成失败'
  } finally { generating.value = false }
}

async function doApply() {
  if (!reportName.value.trim()) { ElMessage.warning('请先填写报表名称'); return }
  applying.value = true
  error.value = ''
  try {
    const res: any = await request.post('/report/add', {
      name: reportName.value,
      groupName: groupName.value,
      sourceType: 'dataview',
      sourceRef: '',
      customSql: '',
      paramsConfig: JSON.stringify(preview.value.paramsList || []),
      layoutJson: preview.value.layoutJson,
      status: 1
    })
    applied.value = { id: res.data, name: reportName.value }
    ElMessage.success('报表已生成')
  } catch (e: any) {
    error.value = e?.message || '生成失败'
  } finally { applying.value = false }
}

function openReportView(id: number) {
  window.open(`/report/view/${id}`, '_blank')
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box;overflow:auto }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
.err-box { margin-top:10px;padding:8px 12px;background:#fef0f0;border:1px solid #fbc4c4;border-radius:6px;color:#f56c6c;font-size:12px;white-space:pre-wrap }
</style>
