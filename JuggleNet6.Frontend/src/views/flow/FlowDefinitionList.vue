<template>
  <div class="page-container">
    <div class="page-header">
      <h2>流程定义</h2>
      <div style="display:flex;gap:8px">
        <el-button icon="Download" :disabled="selectedRows.length === 0" @click="generateWord">生成对接文档</el-button>
        <el-button icon="Upload" @click="triggerImport">导入</el-button>
        <el-button type="primary" icon="Plus" @click="openAdd">新建流程</el-button>
      </div>
    </div>
    <!-- 隐藏的文件输入框（用于导入） -->
    <input ref="importFileRef" type="file" accept=".json" style="display:none" @change="doImport" />

    <!-- 搜索 -->
    <el-card class="search-card">
      <el-form inline>
        <el-form-item label="流程名称">
          <el-input v-model="searchForm.flowName" placeholder="请输入流程名称" clearable />
        </el-form-item>
        <el-form-item label="分组">
          <el-select v-model="searchForm.groupName" placeholder="全部分组" clearable style="width:160px">
            <el-option v-for="g in groupList" :key="g" :label="g" :value="g" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" icon="Search" @click="loadData">查询</el-button>
          <el-button icon="Refresh" @click="reset">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 表格 -->
    <el-card class="table-card">
      <el-table :data="tableData" stripe v-loading="loading" height="100%"
        @selection-change="(rows: any) => selectedRows = rows">
        <el-table-column type="selection" width="45" />
        <el-table-column prop="flowKey" label="流程Key" width="200" show-overflow-tooltip />
        <el-table-column prop="flowName" label="流程名称" />
        <el-table-column prop="flowType" label="类型" width="80">
          <template #default="{ row }">
            <el-tag :type="row.flowType === 'sync' ? 'success' : 'warning'" size="small">
              {{ row.flowType === 'sync' ? '同步' : '异步' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="status" label="状态" width="80">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'info'" size="small">
              {{ row.status === 1 ? '已部署' : '草稿' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="flowDesc" label="描述" show-overflow-tooltip />
        <el-table-column prop="groupName" label="分组" width="120" show-overflow-tooltip>
          <template #default="{ row }">
            <el-tag v-if="row.groupName" size="small" type="info">{{ row.groupName }}</el-tag>
            <span v-else style="color:#ccc">-</span>
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" width="180" show-overflow-tooltip />
        <el-table-column label="操作" width="370" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" link @click="goDesign(row)">设计</el-button>
            <el-button size="small" type="success" link @click="doDeploy(row)">部署</el-button>
            <el-button size="small" link @click="openEdit(row)">编辑</el-button>
            <el-button size="small" type="info" link @click="doClone(row)">克隆</el-button>
            <el-button size="small" type="warning" link @click="doExport(row)">导出</el-button>
            <el-button size="small" type="info" link @click="openPublish(row)">发布</el-button>
            <el-tooltip v-if="row.serviceAlias" :content="`WSDL: /open/services/${row.serviceAlias}/wsdl`">
              <el-button size="small" link @click="openServiceWsdl(row)">WSDL</el-button>
            </el-tooltip>
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

    <!-- 新建/编辑弹窗 -->
    <el-dialog v-model="dialogVisible" :title="isEdit ? '编辑流程' : '新建流程'" width="540px">
      <el-form ref="formRef" :model="form" :rules="rules" label-width="80px">
        <el-form-item label="流程名称" prop="flowName">
          <el-input v-model="form.flowName" placeholder="请输入流程名称" />
        </el-form-item>
        <el-form-item label="流程类型" prop="flowType">
          <el-radio-group v-model="form.flowType">
            <el-radio value="sync">同步</el-radio>
            <el-radio value="async">异步</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="访问别名">
          <el-input v-model="form.serviceAlias" placeholder="可选，设置后通过 /open/services/别名 访问" />
          <div style="font-size:11px;color:#999;margin-top:2px">
            设置后可通过 GET/POST /open/services/{{ form.serviceAlias || '别名' }} 触发最新版本
          </div>
        </el-form-item>
        <el-form-item label="分组">
          <el-select v-model="form.groupName" placeholder="选择或输入分组" clearable filterable allow-create style="width:100%">
            <el-option v-for="g in groupList" :key="g" :label="g" :value="g" />
          </el-select>
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.flowDesc" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSubmit">确认</el-button>
      </template>
    </el-dialog>

    <!-- 发布到市场 -->
    <MarketPublishDialog v-model:visible="publishVisible" item-type="flow"
      :default-name="publishForm.itemName" :default-desc="publishForm.description" :default-group="publishForm.groupName"
      :content-json="publishForm.contentJson" />
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import request from '../../utils/request'
import { Document, Packer, Paragraph, Table, TableRow, TableCell, WidthType, HeadingLevel, AlignmentType, BorderStyle } from 'docx'
import { saveAs } from 'file-saver'
import MarketPublishDialog from '../../components/MarketPublishDialog.vue'

// 发布到市场
const publishVisible = ref(false)
const publishForm = ref<any>({ itemName: '', description: '', groupName: '', contentJson: '{}' })
function openPublish(row: any) {
  publishForm.value = {
    itemName: row.flowName || '',
    description: row.flowDesc || '',
    groupName: row.groupName || '',
    contentJson: JSON.stringify({
      flowName: row.flowName, flowDesc: row.flowDesc, groupName: row.groupName, flowContent: row.flowContent || '[]'
    })
  }
  publishVisible.value = true
}

const router = useRouter()
const loading = ref(false)
const tableData = ref([])
const selectedRows = ref<any[]>([])
const searchForm = reactive({ flowName: '', groupName: '' })
const groupList = ref<string[]>([])
const page = reactive({ num: 1, size: 10, total: 0 })
const dialogVisible = ref(false)
const isEdit = ref(false)
const formRef = ref()
const importFileRef = ref<HTMLInputElement>()
const form = reactive({ id: 0, flowName: '', flowType: 'sync', flowDesc: '', groupName: '', serviceAlias: '' })
const rules = { flowName: [{ required: true, message: '请输入流程名称', trigger: 'blur' }] }

onMounted(() => { loadData(); loadGroups() })

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.post('/flow/definition/page', {
      pageNum: page.num, pageSize: page.size, flowName: searchForm.flowName, groupName: searchForm.groupName || undefined
    })
    tableData.value = res.data.records
    page.total = res.data.total
  } finally { loading.value = false }
}

async function loadGroups() {
  try {
    const res: any = await request.get('/flow/definition/groups')
    groupList.value = res.data || []
  } catch {}
}

function reset() {
  searchForm.flowName = ''
  searchForm.groupName = ''
  page.num = 1
  loadData()
}

function openAdd() {
  isEdit.value = false
  Object.assign(form, { id: 0, flowName: '', flowType: 'sync', flowDesc: '', groupName: '', serviceAlias: '' })
  dialogVisible.value = true
}

function openEdit(row: any) {
  isEdit.value = true
  Object.assign(form, { id: row.id, flowName: row.flowName, flowType: row.flowType, flowDesc: row.flowDesc, groupName: row.groupName || '', serviceAlias: row.serviceAlias || '' })
  dialogVisible.value = true
}

async function handleSubmit() {
  await formRef.value?.validate()
  if (isEdit.value) {
    await request.put('/flow/definition/update', form)
    ElMessage.success('修改成功')
  } else {
    await request.post('/flow/definition/add', form)
    ElMessage.success('创建成功')
  }
  dialogVisible.value = false
  loadData()
}

async function doClone(row: any) {
  await ElMessageBox.confirm(`确认克隆流程「${row.flowName}」为新草稿？`, '提示', { type: 'info' })
  const res: any = await request.post(`/flow/definition/clone/${row.id}`, {})
  ElMessage.success(`克隆成功：${res.data?.flowName || ''}`)
  loadData()
}

function openServiceWsdl(row: any) {
  const baseUrl = window.location.origin
  window.open(`${baseUrl}/open/services/${row.serviceAlias}/wsdl`, '_blank')
}

async function doDelete(row: any) {
  await ElMessageBox.confirm(`确认删除流程「${row.flowName}」？`, '提示', { type: 'warning' })
  await request.delete(`/flow/definition/delete/${row.id}`)
  ElMessage.success('删除成功')
  loadData()
}

async function doDeploy(row: any) {
  await ElMessageBox.confirm(`确认部署流程「${row.flowName}」？`, '提示', { type: 'warning' })
  await request.post('/flow/definition/deploy', { flowDefinitionId: row.id })
  ElMessage.success('部署成功')
  loadData()
}

function goDesign(row: any) {
  router.push(`/design/${row.flowKey}`)
}

// ===== 导出 =====
async function doExport(row: any) {
  try {
    // 使用 fetch 直接下载文件（绕过 axios 的 JSON 解析）
    const token = localStorage.getItem('token') || ''
    const resp = await fetch(`/api/flow/definition/export/${row.id}`, {
      headers: { Authorization: `Bearer ${token}` }
    })
    if (!resp.ok) { ElMessage.error('导出失败'); return }
    const blob = await resp.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `flow_${row.flowKey}_${Date.now()}.json`
    a.click()
    URL.revokeObjectURL(url)
    ElMessage.success('导出成功')
  } catch (e) {
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
    if (json.exportType !== 'flow') { ElMessage.error('文件格式不正确，请选择流程定义的导出文件'); return }
    const res: any = await request.post('/flow/definition/import', json)
    ElMessage.success(`导入成功：${res.data?.flowName || ''}`)
    loadData()
  } catch (ex: any) {
    ElMessage.error('导入失败：' + (ex?.message || ex))
  }
}
// ===== 生成对接Word文档 =====
async function generateWord() {
  try {
    const docData: any[] = []
    for (const row of selectedRows.value) {
      const res: any = await request.get(`/flow/definition/info/${row.id}`)
      const d = res.data
      docData.push({
        flowKey: d.definition?.flowKey || row.flowKey,
        flowName: d.definition?.flowName || row.flowName,
        flowDesc: d.definition?.flowDesc || row.flowDesc || '',
        flowType: d.definition?.flowType || row.flowType || 'sync',
        inputParams: d.inputParams || [],
        outputParams: d.outputParams || [],
        variables: d.variables || []
      })
    }

    const children: any[] = []
    // 标题
    children.push(new Paragraph({
      text: 'Juggle 接口编排平台 - 流程对接文档',
      heading: HeadingLevel.TITLE,
      alignment: AlignmentType.CENTER,
      spacing: { after: 200 }
    }))
    children.push(new Paragraph({
      text: `生成时间：${new Date().toLocaleString()}`,
      alignment: AlignmentType.CENTER,
      spacing: { after: 400 },
      run: { size: 20, color: '888888' }
    }))

    const noBorder = { top: { style: BorderStyle.SINGLE, size: 1, color: 'cccccc' }, bottom: { style: BorderStyle.SINGLE, size: 1, color: 'cccccc' }, left: { style: BorderStyle.SINGLE, size: 1, color: 'cccccc' }, right: { style: BorderStyle.SINGLE, size: 1, color: 'cccccc' } }

    for (const flow of docData) {
      // 流程标题
      children.push(new Paragraph({
        text: `流程：${flow.flowName}（${flow.flowKey}）`,
        heading: HeadingLevel.HEADING_2,
        spacing: { before: 300, after: 100 }
      }))
      if (flow.flowDesc) children.push(new Paragraph({ text: `描述：${flow.flowDesc}`, spacing: { after: 100 } }))
      children.push(new Paragraph({ text: `类型：${flow.flowType === 'sync' ? '同步' : '异步'}`, spacing: { after: 100 } }))
      children.push(new Paragraph({ text: `调用地址：POST /open/flow/trigger/${flow.flowKey}`, spacing: { after: 100 }, run: { font: 'Courier New' } }))
      children.push(new Paragraph({ text: `请求头：X-Access-Token: <token>`, spacing: { after: 200 } }))

      // 入参表格
      if (flow.inputParams.length > 0) {
        children.push(new Paragraph({ text: '入参说明：', heading: HeadingLevel.HEADING_3, spacing: { before: 200, after: 100 } }))
        const inHeaderRow = new TableRow({
          children: ['参数名', '参数编码', '数据类型', '必填', '默认值', '说明'].map(t =>
            new TableCell({ children: [new Paragraph({ text: t, run: { bold: true } })], width: { size: t === '说明' ? 3000 : 1600, type: WidthType.DXA }, borders: noBorder })
          )
        })
        const inParamRows = flow.inputParams.map((p: any) =>
          new TableRow({
            children: [p.paramName || '', p.paramCode || '', p.dataType || '', p.required === 1 ? '是' : '否', p.defaultValue || '', p.remark || ''].map(t =>
              new TableCell({ children: [new Paragraph({ text: String(t) })], width: { size: 1600, type: WidthType.DXA }, borders: noBorder })
            )
          })
        )
        children.push(new Table({ rows: [inHeaderRow, ...inParamRows], width: { size: 100, type: WidthType.PERCENTAGE } }))
      }

      // 出参表格
      if (flow.outputParams.length > 0) {
        children.push(new Paragraph({ text: '出参说明：', heading: HeadingLevel.HEADING_3, spacing: { before: 200, after: 100 } }))
        const outHeaderRow = new TableRow({
          children: ['参数名', '参数编码', '数据类型', '说明'].map(t =>
            new TableCell({ children: [new Paragraph({ text: t, run: { bold: true } })], width: { size: t === '说明' ? 4000 : 2000, type: WidthType.DXA }, borders: noBorder })
          )
        })
        const outParamRows = flow.outputParams.map((p: any) =>
          new TableRow({
            children: [p.paramName || '', p.paramCode || '', p.dataType || '', p.remark || ''].map(t =>
              new TableCell({ children: [new Paragraph({ text: String(t) })], width: { size: 2000, type: WidthType.DXA }, borders: noBorder })
            )
          })
        )
        children.push(new Table({ rows: [outHeaderRow, ...outParamRows], width: { size: 100, type: WidthType.PERCENTAGE } }))
      }

      // 变量表格
      if (flow.variables.length > 0) {
        children.push(new Paragraph({ text: '内部变量：', heading: HeadingLevel.HEADING_3, spacing: { before: 200, after: 100 } }))
        const headerRow = new TableRow({
          children: ['变量名', '变量编码', '数据类型', '默认值'].map(t =>
            new TableCell({ children: [new Paragraph({ text: t, run: { bold: true } })], width: { size: 25, type: WidthType.PERCENTAGE }, borders: noBorder })
          )
        })
        const varRows = flow.variables.map((v: any) =>
          new TableRow({
            children: [v.variableName || '', v.variableCode || '', v.dataType || '', v.defaultValue || ''].map(t =>
              new TableCell({ children: [new Paragraph({ text: String(t) })], width: { size: 25, type: WidthType.PERCENTAGE }, borders: noBorder })
            )
          })
        )
        children.push(new Table({ rows: [headerRow, ...varRows], width: { size: 100, type: WidthType.PERCENTAGE } }))
      }
    }

    const doc = new Document({ sections: [{ properties: {}, children }] })
    const blob = await Packer.toBlob(doc)
    saveAs(blob, `流程对接文档_${new Date().toISOString().slice(0,10)}.docx`)
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

