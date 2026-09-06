<template>
  <div class="rv-container">
    <div class="rv-header">
      <h2>{{ reportName || '报表' }}</h2>
      <div style="display:flex;gap:8px">
        <el-button size="small" type="primary" :loading="loading" @click="doQuery">查询</el-button>
        <el-button size="small" type="success" :loading="exporting" @click="doExportExcel">导出 Excel</el-button>
        <el-button size="small" type="warning" :loading="exporting" @click="doExportPdf">导出 PDF</el-button>
        <el-button size="small" @click="doPrint">打印</el-button>
      </div>
    </div>

    <!-- 查询条件 -->
    <el-card v-if="params.length>0" style="margin-bottom:12px" shadow="never">
      <ReportParams :params="params" :values="values" :report-id="reportId" />
    </el-card>

    <!-- 报表内容 -->
    <el-card shadow="never">
      <div v-loading="loading" v-if="html" v-html="html" style="overflow:auto;max-height:calc(100vh - 260px)" />
      <el-empty v-else-if="!loading" description="点击「查询」查看报表" :image-size="80" />
      <div v-loading="loading" v-else style="height:200px" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'
import ReportParams from '../../components/ReportParams.vue'
import { initParamValues, buildParamPayload } from '../../utils/reportParams'

const route = useRoute()
const reportId = Number(route.params.id) || 0
const reportName = ref('')
const params = ref<any[]>([])
const values = ref<Record<string, any>>({})
const html = ref('')
const loading = ref(false)
const exporting = ref(false)

onMounted(async () => {
  try {
    const res = await request.get(`/report/info/${reportId}`)
    reportName.value = res.data?.name || '报表'
    document.title = reportName.value
    try { params.value = JSON.parse(res.data?.paramsConfig || '[]') } catch { params.value = [] }
    // 默认值初始化，URL 查询参数有值时覆盖
    values.value = initParamValues(params.value, route.query as Record<string, any>)
    if (params.value.length === 0) doQuery()   // 无参数报表打开即查
  } catch { ElMessage.error('报表不存在或已删除') }
})

async function doQuery() {
  loading.value = true
  try {
    const res = await request.post('/report/preview', { id: reportId, params: buildParamPayload(params.value, values.value) })
    html.value = res.data?.html || ''
  } finally { loading.value = false }
}

function downloadBlob(data: Blob, filename: string) {
  const url = URL.createObjectURL(data)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

async function doExportExcel() {
  exporting.value = true
  try {
    const res: any = await request.post('/report/export-excel', { id: reportId, params: buildParamPayload(params.value, values.value) }, { responseType: 'blob' })
    downloadBlob(res.data, `${reportName.value}.xlsx`)
  } finally { exporting.value = false }
}

async function doExportPdf() {
  exporting.value = true
  try {
    const res: any = await request.post('/report/export-pdf', { id: reportId, params: buildParamPayload(params.value, values.value) }, { responseType: 'blob' })
    downloadBlob(res.data, `${reportName.value}.pdf`)
  } finally { exporting.value = false }
}

async function doPrint() {
  if (!html.value) { ElMessage.warning('请先查询报表'); return }
  const w = window.open('', '_blank')
  if (!w) { ElMessage.warning('浏览器拦截了弹出窗口，请允许后重试'); return }
  w.document.write(`<html><head><title>${reportName.value}</title><meta charset="utf-8"><style>@media print{@page{size:A4;margin:15mm}}body{font-family:'Microsoft YaHei',sans-serif}</style></head><body>${html.value}</body></html>`)
  w.document.close()
  w.focus()
  setTimeout(() => { w.print() }, 300)
}
</script>

<style scoped>
.rv-container { padding: 16px; height: 100%; display: flex; flex-direction: column; box-sizing: border-box; }
.rv-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; flex-shrink: 0; }
.rv-header h2 { margin: 0; }
</style>
