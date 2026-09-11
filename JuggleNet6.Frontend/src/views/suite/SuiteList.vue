<template>
  <div class="page-container">
    <div class="page-header">
      <h2>套件管理</h2>
      <div style="display:flex;gap:8px">
        <el-button icon="Download" :disabled="selectedRows.length === 0" @click="generateWord">生成对接文档</el-button>
        <el-button icon="Upload" @click="triggerImport">导入</el-button>
        <el-button type="primary" icon="Plus" @click="openAdd">新建套件</el-button>
      </div>
    </div>
    <!-- 隐藏文件输入框 -->
    <input ref="importFileRef" type="file" accept=".json" style="display:none" @change="doImport" />

    <el-card class="search-card">
      <el-form inline>
        <el-form-item label="套件名称">
          <el-input v-model="searchForm.suiteName" placeholder="请输入套件名称" clearable />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" icon="Search" @click="loadData">查询</el-button>
          <el-button icon="Refresh" @click="reset">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card class="table-card">
      <el-table :data="tableData" stripe v-loading="loading" height="100%"
        @selection-change="(rows: any) => selectedRows = rows">
        <el-table-column type="selection" width="45" />
        <el-table-column prop="suiteCode" label="套件Code" width="220" show-overflow-tooltip />
        <el-table-column prop="suiteName" label="套件名称" />
        <el-table-column prop="suiteVersion" label="版本" width="80" />
        <el-table-column prop="suiteDesc" label="描述" show-overflow-tooltip />
        <el-table-column prop="createdAt" label="创建时间" width="180" show-overflow-tooltip />
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" link @click="goApiList(row)">接口管理</el-button>
            <el-button size="small" link @click="openEdit(row)">编辑</el-button>
            <el-button size="small" type="info" link @click="openPublish(row)">发布</el-button>
            <el-button size="small" type="warning" link @click="doExport(row)">导出</el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div class="pagination-bar">
        <el-pagination v-model:current-page="page.num" v-model:page-size="page.size"
          :total="page.total" layout="total,prev,pager,next"
          @current-change="loadData" />
      </div>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="isEdit ? '编辑套件' : '新建套件'" width="480px">
      <el-form ref="formRef" :model="form" :rules="rules" label-width="80px">
        <el-form-item label="套件名称" prop="suiteName">
          <el-input v-model="form.suiteName" />
        </el-form-item>
        <el-form-item label="版本">
          <el-input v-model="form.suiteVersion" placeholder="v1.0.0" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.suiteDesc" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSubmit">确认</el-button>
      </template>
    </el-dialog>

    <!-- 发布到市场 -->
    <MarketPublishDialog v-model:visible="publishVisible" item-type="api"
      :default-name="publishForm.itemName" :default-desc="publishForm.description" :default-group="publishForm.groupName"
      :content-json="publishForm.contentJson" />
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import request from '../../utils/request'
import MarketPublishDialog from '../../components/MarketPublishDialog.vue'

// 发布到市场（套件及其全部接口）
const publishVisible = ref(false)
const publishForm = ref<any>({ itemName: '', description: '', groupName: '', contentJson: '{}' })
async function openPublish(row: any) {
  try {
    const apisRes: any = await request.post('/suite/api/list', { suiteCode: row.suiteCode })
    const apis = (apisRes.data || []).map((a: any) => ({
      suiteCode: a.suiteCode, methodCode: a.methodCode, methodName: a.methodName,
      methodDesc: a.methodDesc, url: a.url, requestType: a.requestType || 'POST'
    }))
    publishForm.value = {
      itemName: row.suiteName || row.suiteCode || '',
      description: row.suiteDesc || '',
      groupName: '',
      contentJson: JSON.stringify({
        suites: [{ suiteCode: row.suiteCode, suiteName: row.suiteName, suiteDesc: row.suiteDesc }], apis
      })
    }
    publishVisible.value = true
  } catch { /* 拦截器已提示 */ }
}
import { Document, Packer, Paragraph, Table, TableRow, TableCell, WidthType, HeadingLevel, AlignmentType, BorderStyle } from 'docx'
import { saveAs } from 'file-saver'

const router = useRouter()
const loading = ref(false)
const tableData = ref([])
const selectedRows = ref<any[]>([])
const searchForm = reactive({ suiteName: '' })
const page = reactive({ num: 1, size: 10, total: 0 })
const dialogVisible = ref(false)
const isEdit = ref(false)
const formRef = ref()
const importFileRef = ref<HTMLInputElement>()
const form = reactive({ id: 0, suiteName: '', suiteVersion: 'v1.0.0', suiteDesc: '' })
const rules = { suiteName: [{ required: true, message: '请输入套件名称', trigger: 'blur' }] }

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.post('/suite/page', {
      pageNum: page.num, pageSize: page.size, suiteName: searchForm.suiteName
    })
    tableData.value = res.data.records
    page.total = res.data.total
  } finally { loading.value = false }
}

function reset() { searchForm.suiteName = ''; page.num = 1; loadData() }

function openAdd() {
  isEdit.value = false
  Object.assign(form, { id: 0, suiteName: '', suiteVersion: 'v1.0.0', suiteDesc: '' })
  dialogVisible.value = true
}

function openEdit(row: any) {
  isEdit.value = true
  Object.assign(form, { id: row.id, suiteName: row.suiteName, suiteVersion: row.suiteVersion, suiteDesc: row.suiteDesc })
  dialogVisible.value = true
}

async function handleSubmit() {
  await formRef.value?.validate()
  if (isEdit.value) {
    await request.put('/suite/update', form)
    ElMessage.success('修改成功')
  } else {
    await request.post('/suite/add', form)
    ElMessage.success('创建成功')
  }
  dialogVisible.value = false
  loadData()
}

async function doDelete(row: any) {
  await ElMessageBox.confirm(`确认删除套件「${row.suiteName}」？`, '提示', { type: 'warning' })
  await request.delete(`/suite/delete/${row.id}`)
  ElMessage.success('删除成功')
  loadData()
}

function goApiList(row: any) {
  router.push(`/suite/api/${row.suiteCode}/${row.id}`)
}

// ===== 导出 =====
async function doExport(row: any) {
  try {
    const token = localStorage.getItem('token') || ''
    const resp = await fetch(`/api/suite/export/${row.id}`, {
      headers: { Authorization: `Bearer ${token}` }
    })
    if (!resp.ok) { ElMessage.error('导出失败'); return }
    const blob = await resp.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `suite_${row.suiteCode}_${Date.now()}.json`
    a.click()
    URL.revokeObjectURL(url)
    ElMessage.success('导出成功')
  } catch {
    ElMessage.error('导出异常')
  }
}

// ===== 导入 =====
function triggerImport() {
  importFileRef.value!.value = ''
  importFileRef.value!.click()
}

async function doImport(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  if (!file) return
  try {
    const text = await file.text()
    const json = JSON.parse(text)
    if (json.exportType !== 'suite') { ElMessage.error('文件格式不正确，请选择套件的导出文件'); return }
    const res: any = await request.post('/suite/import', json)
    ElMessage.success(`导入成功：${res.data?.suiteName || ''}`)
    loadData()
  } catch (ex: any) {
    ElMessage.error('导入失败：' + (ex?.message || ex))
  }
}
// ===== 生成对接Word文档 =====
async function generateWord() {
  try {
    const noBorder = { top: { style: BorderStyle.SINGLE, size: 1, color: 'cccccc' }, bottom: { style: BorderStyle.SINGLE, size: 1, color: 'cccccc' }, left: { style: BorderStyle.SINGLE, size: 1, color: 'cccccc' }, right: { style: BorderStyle.SINGLE, size: 1, color: 'cccccc' } }
    const children: any[] = []

    children.push(new Paragraph({
      text: 'Juggle 接口编排平台 - 套件接口对接文档',
      heading: HeadingLevel.TITLE, alignment: AlignmentType.CENTER, spacing: { after: 200 }
    }))
    children.push(new Paragraph({
      text: `生成时间：${new Date().toLocaleString()}`,
      alignment: AlignmentType.CENTER, spacing: { after: 400 },
      run: { size: 20, color: '888888' }
    }))

    for (const suite of selectedRows.value) {
      // 加载套件下的所有 API
      const apisRes: any = await request.post('/suite/api/list', { suiteCode: suite.suiteCode })
      const apis = apisRes.data || []

      children.push(new Paragraph({
        text: `套件：${suite.suiteName}（${suite.suiteCode}）`,
        heading: HeadingLevel.HEADING_2, spacing: { before: 300, after: 100 }
      }))
      if (suite.suiteDesc) children.push(new Paragraph({ text: `描述：${suite.suiteDesc}`, spacing: { after: 100 } }))
      children.push(new Paragraph({ text: `版本：${suite.suiteVersion || '-'}`, spacing: { after: 200 } }))

      for (const api of apis) {
        // 加载 API 的参数
        let inputParams: any[] = [], outputParams: any[] = [], headerParams: any[] = []
        try {
          const infoRes: any = await request.get(`/suite/api/info/${api.id}`)
          inputParams = infoRes.data?.inputParams || []
          outputParams = infoRes.data?.outputParams || []
          headerParams = infoRes.data?.headerParams || []
        } catch {}

        children.push(new Paragraph({
          text: `接口：${api.methodName || api.methodCode}`,
          heading: HeadingLevel.HEADING_3, spacing: { before: 200, after: 100 }
        }))
        if (api.methodDesc) children.push(new Paragraph({ text: `描述：${api.methodDesc}`, spacing: { after: 50 } }))
        children.push(new Paragraph({ text: `请求方式：${api.requestType || 'GET'}    URL：${api.url || '-'}`, spacing: { after: 50 }, run: { font: 'Courier New' } }))
        children.push(new Paragraph({ text: `Content-Type：${api.contentType || 'JSON'}`, spacing: { after: 150 } }))

        // Header 参数
        if (headerParams.length > 0) {
          children.push(new Paragraph({ text: 'Header 参数：', spacing: { after: 50 }, run: { bold: true } }))
          const hRow = new TableRow({ children: ['参数名', '参数编码', '类型', '默认值'].map(t => new TableCell({ children: [new Paragraph({ text: t, run: { bold: true } })], width: { size: 25, type: WidthType.PERCENTAGE }, borders: noBorder })) })
          const hRows = headerParams.map((p: any) => new TableRow({ children: [p.paramName || '', p.paramCode || '', p.dataType || '', p.defaultValue || ''].map(t => new TableCell({ children: [new Paragraph({ text: String(t) })], width: { size: 25, type: WidthType.PERCENTAGE }, borders: noBorder })) }))
          children.push(new Table({ rows: [hRow, ...hRows], width: { size: 100, type: WidthType.PERCENTAGE } }))
        }

        // 入参
        if (inputParams.length > 0) {
          children.push(new Paragraph({ text: '入参说明：', spacing: { before: 100, after: 50 }, run: { bold: true } }))
          const iRow = new TableRow({ children: ['参数名', '参数编码', '数据类型', '必填', '默认值'].map(t => new TableCell({ children: [new Paragraph({ text: t, run: { bold: true } })], width: { size: 20, type: WidthType.PERCENTAGE }, borders: noBorder })) })
          const iRows = inputParams.map((p: any) => new TableRow({ children: [p.paramName || '', p.paramCode || '', p.dataType || '', p.required === 1 ? '是' : '否', p.defaultValue || ''].map(t => new TableCell({ children: [new Paragraph({ text: String(t) })], width: { size: 20, type: WidthType.PERCENTAGE }, borders: noBorder })) }))
          children.push(new Table({ rows: [iRow, ...iRows], width: { size: 100, type: WidthType.PERCENTAGE } }))
        }

        // 出参
        if (outputParams.length > 0) {
          children.push(new Paragraph({ text: '出参说明：', spacing: { before: 100, after: 50 }, run: { bold: true } }))
          const oRow = new TableRow({ children: ['参数名', '参数编码', '数据类型', '说明'].map(t => new TableCell({ children: [new Paragraph({ text: t, run: { bold: true } })], width: { size: 25, type: WidthType.PERCENTAGE }, borders: noBorder })) })
          const oRows = outputParams.map((p: any) => new TableRow({ children: [p.paramName || '', p.paramCode || '', p.dataType || '', p.remark || ''].map(t => new TableCell({ children: [new Paragraph({ text: String(t) })], width: { size: 25, type: WidthType.PERCENTAGE }, borders: noBorder })) }))
          children.push(new Table({ rows: [oRow, ...oRows], width: { size: 100, type: WidthType.PERCENTAGE } }))
        }
      }
    }

    const doc = new Document({ sections: [{ properties: {}, children }] })
    const blob = await Packer.toBlob(doc)
    saveAs(blob, `套件接口对接文档_${new Date().toISOString().slice(0,10)}.docx`)
    ElMessage.success('文档生成成功')
  } catch (ex: any) {
    ElMessage.error('生成文档失败：' + (ex?.message || ex))
  }
}
</script>

<style scoped>
.page-container {
  padding: 16px;
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  box-sizing: border-box;
}
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
  flex-shrink: 0;
}
.page-header h2 { font-size: 20px; color: #333; }
.search-card { flex-shrink: 0; margin-bottom: 12px; }
.table-card {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.table-card :deep(.el-card__body) {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding-bottom: 0;
}
.table-card :deep(.el-table) {
  flex: 1;
  min-height: 0;
}
.pagination-bar {
  flex-shrink: 0;
  padding: 10px 0 2px;
  display: flex;
  justify-content: flex-end;
}
</style>

