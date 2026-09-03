<template>
  <div class="page-container">
    <div class="page-header">
      <h2>数据视图</h2>
      <el-button type="primary" size="small" @click="openAdd">添加视图</el-button>
    </div>
    <el-card>
      <div style="display:flex;gap:12px;margin-bottom:12px">
        <el-input v-model="keyword" placeholder="搜索..." size="small" style="width:200px" clearable @change="loadData" />
        <el-select v-model="groupFilter" placeholder="分组" size="small" style="width:150px" clearable @change="loadData">
          <el-option v-for="g in groups" :key="g" :label="g" :value="g" />
        </el-select>
      </div>
      <el-table :data="tableData" v-loading="loading">
        <el-table-column prop="groupName" label="分组" width="120" />
        <el-table-column prop="name" label="名称" width="180" />
        <el-table-column prop="dataSourceId" label="数据源ID" width="90" />
        <el-table-column prop="sql" label="SQL" show-overflow-tooltip />
        <el-table-column label="状态" width="70">
          <template #default="{ row }"><el-tag :type="row.status===1?'success':'info'" size="small">{{ row.status===1?'启用':'停用' }}</el-tag></template>
        </el-table-column>
        <el-table-column label="操作" width="200">
          <template #default="{ row }">
            <el-button size="small" link @click="openEdit(row)">编辑</el-button>
            <el-button size="small" type="primary" link @click="openPreview(row)">预览</el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination v-model:current-page="page" :page-size="20" layout="prev,next" :total="total" @change="loadData" style="margin-top:12px;justify-content:flex-end" />
    </el-card>

    <el-dialog v-model="dialogVisible" :title="isEdit?'编辑视图':'添加视图'" width="820px">
      <el-form :model="form" label-width="100px">
        <el-form-item label="分组"><el-input v-model="form.groupName" placeholder="如: 销售报表" /></el-form-item>
        <el-form-item label="名称"><el-input v-model="form.name" placeholder="如: 月度销售汇总" /></el-form-item>
        <el-form-item label="数据源">
          <el-select v-model="form.dataSourceId" style="width:100%">
            <el-option v-for="ds in dsList" :key="ds.id" :label="`${ds.dataSourceName} (${ds.dataSourceType})`" :value="ds.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="SQL">
          <div style="width:100%">
            <div style="display:flex;gap:6px;margin-bottom:6px">
              <el-button size="small" icon="Collection" @click="openDbBrowser">表/视图/存储过程</el-button>
              <el-button size="small" icon="VideoPlay" type="primary" plain @click="openTest">测试 SQL</el-button>
            </div>
            <el-input v-model="form.sql" type="textarea" :rows="5" placeholder="SELECT * FROM t WHERE name LIKE @keyword" class="code-editor" />
          </div>
        </el-form-item>
        <el-form-item label="参数">
          <div v-for="(p, i) in paramList" :key="i" style="display:flex;gap:8px;margin-bottom:4px">
            <el-input v-model="p.name" placeholder="参数名" size="small" style="width:130px" />
            <el-select v-model="p.type" size="small" style="width:100px"><el-option v-for="t in ['string','int','date']" :key="t" :label="t" :value="t" /></el-select>
            <el-input v-model="p.label" placeholder="显示名" size="small" style="width:130px" />
            <el-input v-model="p.default" placeholder="默认值" size="small" style="width:120px" />
            <el-button size="small" type="danger" link @click="paramList.splice(i,1)">删</el-button>
          </div>
          <el-button size="small" @click="paramList.push({name:'',type:'string',label:'',default:''})">+添加参数</el-button>
        </el-form-item>
        <el-form-item label="字段中文对照">
          <div style="width:100%">
            <div style="display:flex;gap:6px;margin-bottom:6px">
              <el-button size="small" icon="MagicStick" @click="generateMapping" :loading="mappingLoading">自动生成</el-button>
              <span style="font-size:12px;color:#909399;line-height:24px">格式：字段=中文注释，一行一条</span>
            </div>
            <el-input v-model="form.columnMapping" type="textarea" :rows="5"
              placeholder="自动生成：单表取字段中文注释（无注释则 字段=字段），其它 SQL 按实际查询列生成 字段=字段" class="code-editor" />
          </div>
        </el-form-item>
        <el-form-item label="备注"><el-input v-model="form.remark" type="textarea" :rows="2" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible=false">取消</el-button>
        <el-button type="primary" @click="doSubmit">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="previewVisible" title="预览数据" width="800px">
      <div v-if="previewParams.length>0" style="display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap">
        <div v-for="p in previewParams" :key="p.name" style="display:flex;align-items:center;gap:4px">
          <span style="font-size:12px">{{ p.label || p.name }}:</span>
          <el-input v-model="previewValues[p.name]" size="small" style="width:140px" />
        </div>
        <el-button size="small" type="primary" @click="doPreview">查询</el-button>
      </div>
      <el-table :data="previewRows" max-height="400" border size="small">
        <el-table-column v-for="col in previewColumns" :key="col" :prop="col" :label="col" show-overflow-tooltip />
      </el-table>
      <div style="margin-top:8px;color:#888;font-size:12px">共 {{ previewRows.length }} 条</div>
    </el-dialog>

    <!-- 数据库对象浏览（辅助生成 SQL） -->
    <DbObjectBrowser v-model:visible="dbBrowserVisible"
      :data-source-name="selectedDs()?.dataSourceName || ''"
      :data-source-type="selectedDs()?.dataSourceType || 'mysql'"
      @generated="sql => form.sql = sql" />

    <!-- 测试 SQL -->
    <SqlTestDialog v-model:visible="testVisible"
      :data-source-id="form.dataSourceId" :sql="form.sql" :params="paramList" />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import request from '../../utils/request'
import DbObjectBrowser from '../../components/DbObjectBrowser.vue'
import SqlTestDialog from '../../components/SqlTestDialog.vue'
import { generateColumnMapping } from '../../utils/dbAssist'

const loading = ref(false)
const tableData = ref<any[]>([])
const page = ref(1)
const total = ref(0)
const keyword = ref('')
const groupFilter = ref('')
const dialogVisible = ref(false)
const isEdit = ref(false)
const form = ref<any>({ groupName:'', name:'', dataSourceId:'', sql:'', parameters:'', columnMapping:'', remark:'', status:1 })
const dsList = ref<any[]>([])
const paramList = ref<any[]>([])
const previewVisible = ref(false)
const previewRows = ref<any[]>([])
const previewColumns = ref<string[]>([])
const previewParams = ref<any[]>([])
const previewValues = ref<Record<string,any>>({})
const previewId = ref(0)
const dbBrowserVisible = ref(false)
const testVisible = ref(false)
const mappingLoading = ref(false)

const groups = computed(() => [...new Set(tableData.value.map(r=>r.groupName).filter(Boolean))])

function selectedDs(): any {
  return dsList.value.find((d: any) => d.id === form.value.dataSourceId)
}

onMounted(async () => {
  const [dsRes]: any[] = await Promise.all([request.get('/system/datasource/list')])
  dsList.value = dsRes.data || []
  loadData()
})

async function loadData() {
  loading.value = true
  try {
    const res = await request.post('/report/dataview/page', { pageNum: page.value, pageSize: 20, keyword: keyword.value })
    tableData.value = res.data?.list || []
    total.value = res.data?.total || 0
  } finally { loading.value = false }
}

function openAdd() {
  isEdit.value = false
  form.value = { groupName:'', name:'', dataSourceId:'', sql:'', parameters:'', columnMapping:'', remark:'', status:1 }
  paramList.value = []
  dialogVisible.value = true
}
function openEdit(row: any) {
  isEdit.value = true
  form.value = { ...row }
  try { paramList.value = JSON.parse(row.parameters||'[]') } catch { paramList.value = [] }
  dialogVisible.value = true
}
async function doSubmit() {
  form.value.parameters = JSON.stringify(paramList.value)
  if (isEdit.value) await request.put('/report/dataview/update', form.value)
  else await request.post('/report/dataview/add', form.value)
  ElMessage.success('保存成功'); dialogVisible.value = false; loadData()
}
async function doDelete(row: any) {
  await ElMessageBox.confirm('确认删除？', '提示', { type: 'warning' })
  await request.delete(`/report/dataview/delete/${row.id}`)
  ElMessage.success('已删除'); loadData()
}
function openPreview(row: any) {
  previewId.value = row.id
  try { previewParams.value = JSON.parse(row.parameters||'[]') } catch { previewParams.value = [] }
  previewValues.value = {}
  for (const p of previewParams.value) previewValues.value[p.name] = p.default || ''
  previewRows.value = []
  previewColumns.value = []
  previewVisible.value = true
}
async function doPreview() {
  const res = await request.post('/report/dataview/preview', { id: previewId.value, params: previewValues.value })
  previewColumns.value = res.data?.columns || []
  previewRows.value = res.data?.rows || []
}

function openDbBrowser() {
  if (!selectedDs()) { ElMessage.warning('请先选择数据源'); return }
  dbBrowserVisible.value = true
}
function openTest() {
  if (!form.value.dataSourceId) { ElMessage.warning('请先选择数据源'); return }
  if (!form.value.sql?.trim()) { ElMessage.warning('请先编写 SQL'); return }
  testVisible.value = true
}

async function generateMapping() {
  const ds = selectedDs()
  if (!ds) { ElMessage.warning('请先选择数据源'); return }
  if (!form.value.sql?.trim()) { ElMessage.warning('请先编写 SQL'); return }
  mappingLoading.value = true
  try {
    const params: Record<string, any> = {}
    for (const p of paramList.value) params[p.name] = p.default || ''
    const lines = await generateColumnMapping({
      dataSourceId: ds.id, dataSourceName: ds.dataSourceName, sql: form.value.sql, params
    })
    form.value.columnMapping = lines.join('\n')
    ElMessage.success(`已生成 ${lines.length} 条字段对照`)
  } catch { ElMessage.error('生成失败，请检查 SQL 与数据源') } finally { mappingLoading.value = false }
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.code-editor :deep(textarea) { font-family: Consolas, Monaco, 'Courier New', monospace; font-size: 12px; }
</style>
