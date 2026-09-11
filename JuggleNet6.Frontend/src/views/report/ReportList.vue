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
            <el-button size="small" type="info" link @click="openPublish(row)">发布</el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination v-model:current-page="page" :page-size="20" layout="prev,next" :total="total" @change="loadData" style="margin-top:12px;justify-content:flex-end" />
    </el-card>

    <!-- 发布到市场 -->
    <MarketPublishDialog v-model:visible="publishVisible" item-type="report"
      :default-name="publishForm.itemName" :default-desc="publishForm.description" :default-group="publishForm.groupName"
      :content-json="publishForm.contentJson" />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import request from '../../utils/request'
import MarketPublishDialog from '../../components/MarketPublishDialog.vue'

// 发布到市场
const publishVisible = ref(false)
const publishForm = ref<any>({ itemName: '', description: '', groupName: '', contentJson: '{}' })
function openPublish(row: any) {
  publishForm.value = {
    itemName: row.name || '',
    description: row.groupName || '',
    groupName: row.groupName || '',
    contentJson: JSON.stringify({
      name: row.name, groupName: row.groupName, paramsConfig: row.paramsConfig || '[]', layoutJson: row.layoutJson || '{}'
    })
  }
  publishVisible.value = true
}

const router = useRouter()
const loading = ref(false)
const tableData = ref<any[]>([])
const page = ref(1)
const total = ref(0)

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

/** 预览：打开正式报表访问页（新窗口） */
function openPreview(row: any) {
  const w = window.open(`/report/view/${row.id}`, '_blank')
  if (!w) ElMessage.warning('浏览器拦截了弹出窗口，请允许后重试')
}

async function doExportPdf(row: any) {
  const res: any = await request.post('/report/export-pdf', { id: row.id, params: {} }, { responseType: 'blob' })
  downloadBlob(res.data, `${row.name}.pdf`)
}

async function doExportExcel(row: any) {
  const res: any = await request.post('/report/export-excel', { id: row.id, params: {} }, { responseType: 'blob' })
  downloadBlob(res.data, `${row.name}.xlsx`)
}

/** 打印：取预览 HTML 写入新窗口并触发浏览器打印 */
async function doPrint(row: any) {
  try {
    const res = await request.post('/report/preview', { id: row.id, params: {} })
    const html = res.data?.html || ''
    const w = window.open('', '_blank')
    if (!w) { ElMessage.warning('浏览器拦截了弹出窗口，请允许后重试'); return }
    w.document.write(`<html><head><title>${row.name}</title><meta charset="utf-8"><style>@media print{@page{size:A4;margin:15mm}}body{font-family:'Microsoft YaHei',sans-serif}</style></head><body>${html}</body></html>`)
    w.document.close()
    w.focus()
    setTimeout(() => { w.print() }, 300)
  } catch { /* 拦截器已提示 */ }
}

function downloadBlob(data: Blob, filename: string) {
  // 直接使用响应 Blob（保留原始 MIME 类型，避免浏览器按未知类型补 .txt 后缀）
  const url = URL.createObjectURL(data)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
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
