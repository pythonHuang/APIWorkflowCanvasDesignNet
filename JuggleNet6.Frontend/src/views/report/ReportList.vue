<template>
  <div class="page-container">
    <div class="page-header">
      <h2>报表管理</h2>
      <div style="display:flex;gap:8px">
        <el-button size="small" @click="openDesigner()">新建报表</el-button>
        <el-button size="small" type="primary" @click="loadData">刷新</el-button>
      </div>
    </div>
    <el-card>
      <el-table :data="tableData" v-loading="loading">
        <el-table-column prop="groupName" label="分组" width="120" />
        <el-table-column prop="name" label="报表名称" />
        <el-table-column label="数据来源" width="120">
          <template #default="{ row }">
            <el-tag size="small">{{ sourceLabel(row.sourceType) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="70">
          <template #default="{ row }"><el-tag :type="row.status===1?'success':'info'" size="small">{{ row.status===1?'启用':'停用' }}</el-tag></template>
        </el-table-column>
        <el-table-column label="操作" width="320">
          <template #default="{ row }">
            <el-button size="small" link @click="openDesigner(row.id)">设计</el-button>
            <el-button size="small" type="primary" link @click="openPreview(row)">预览</el-button>
            <el-button size="small" type="warning" link @click="doExportPdf(row)">PDF</el-button>
            <el-button size="small" type="success" link @click="doExportExcel(row)">Excel</el-button>
            <el-button size="small" link @click="doPrint(row)">打印</el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination v-model:current-page="page" :page-size="20" layout="prev,next" :total="total" @change="loadData" style="margin-top:12px;justify-content:flex-end" />
    </el-card>

    <el-dialog v-model="previewVisible" title="预览报表" width="90%" top="5vh">
      <div v-if="previewParams.length>0" style="display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap">
        <div v-for="p in previewParams" :key="p.name" style="display:flex;align-items:center;gap:4px">
          <span style="font-size:12px">{{ p.label||p.name }}:</span>
          <el-input v-model="previewValues[p.name]" size="small" style="width:140px" />
        </div>
        <el-button size="small" type="primary" @click="doPreview">查询</el-button>
      </div>
      <div v-if="previewHtml" v-html="previewHtml" style="border:1px solid #eee;padding:16px;overflow:auto;max-height:70vh"></div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import request from '../../utils/request'

const router = useRouter()
const loading = ref(false)
const tableData = ref<any[]>([])
const page = ref(1)
const total = ref(0)
const previewVisible = ref(false)
const previewHtml = ref('')
const previewParams = ref<any[]>([])
const previewValues = ref<Record<string,any>>({})
const previewId = ref(0)

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res = await request.post('/report/page', { pageNum: page.value, pageSize: 20 })
    tableData.value = res.data?.list || []
    total.value = res.data?.total || 0
  } finally { loading.value = false }
}

function sourceLabel(t: string) {
  const m: Record<string,string> = { flow:'流程', api:'接口', dataview:'数据视图', sql:'自定义SQL' }
  return m[t] || t
}

function openDesigner(id?: number) {
  if (id) router.push(`/report/designer/${id}`)
  else router.push('/report/designer/0')
}

function openPreview(row: any) {
  previewId.value = row.id
  try { previewParams.value = JSON.parse(row.paramsConfig||'[]') } catch { previewParams.value = [] }
  previewValues.value = {}
  for (const p of previewParams.value) previewValues.value[p.name] = p.default || ''
  previewHtml.value = ''
  previewVisible.value = true
}

async function doPreview() {
  const res = await request.post('/report/preview', { id: previewId.value, params: previewValues.value })
  previewHtml.value = res.data?.html || ''
}

async function doExportPdf(row: any) {
  const res = await request.post('/report/export-pdf', { id: row.id, params: {} }, { responseType: 'blob' })
  downloadBlob(res, `${row.name}.html`)
}

async function doExportExcel(row: any) {
  const res = await request.post('/report/export-excel', { id: row.id, params: {} }, { responseType: 'blob' })
  downloadBlob(res, `${row.name}.xls`)
}

function doPrint(_row: any) {
  window.open(`${location.origin}/api/report/export-pdf`, '_blank')
}

function downloadBlob(data: any, filename: string) {
  const url = URL.createObjectURL(new Blob([data]))
  const a = document.createElement('a'); a.href = url; a.download = filename; a.click()
  URL.revokeObjectURL(url)
}

async function doDelete(row: any) {
  await ElMessageBox.confirm('确认删除？', '提示', { type: 'warning' })
  await request.delete(`/report/delete/${row.id}`)
  ElMessage.success('已删除'); loadData()
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
</style>
