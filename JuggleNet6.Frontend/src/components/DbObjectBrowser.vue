<template>
  <el-dialog :model-value="visible" @update:model-value="onVisibleChange" title="🗄 数据库对象（辅助生成 SQL）" width="1000px" append-to-body destroy-on-close>
    <div style="display:flex;gap:10px">
      <div style="flex:1;min-width:0">
        <el-radio-group v-model="tab" size="small" @change="onTabChange">
          <el-radio-button value="tables">表 ({{ tables.length }})</el-radio-button>
          <el-radio-button value="views">视图 ({{ views.length }})</el-radio-button>
          <el-radio-button value="procedures">存储过程 ({{ procedures.length }})</el-radio-button>
        </el-radio-group>
        <el-input v-model="search" placeholder="搜索名称" size="small" clearable style="margin:8px 0" />
        <div class="db-object-list" v-loading="loading">
          <div v-for="obj in filteredObjects" :key="obj.name" class="db-object-item"
            :class="{ active: selectedObject === obj.name }" @click="selectObject(obj.name)">
            <span class="db-object-name">{{ obj.name }}</span>
            <el-button size="small" link type="primary" @click.stop="generateFromObject(obj)">生成SQL</el-button>
          </div>
          <el-empty v-if="!loading && filteredObjects.length === 0" description="暂无对象" :image-size="40" />
        </div>
      </div>
      <div style="width:480px;flex-shrink:0">
        <div class="prop-section-title" style="margin-top:0">
          {{ tab === 'procedures' ? '参数列表' : '字段列表' }}
          <span v-if="selectedObject" style="font-size:11px;color:#909399;font-weight:normal;margin-left:6px">{{ selectedObject }}</span>
          <el-button v-if="tab === 'procedures'" size="small" icon="VideoPlay" type="primary" link @click="generateCallFromSelected" style="margin-left:auto">生成调用语句</el-button>
        </div>

        <!-- 表：字段名称/中文注释/类型/默认值/是否必填 -->
        <el-table v-if="tab === 'tables'" :data="columns" size="small" border max-height="380" v-loading="panelLoading">
          <el-table-column prop="name" label="字段名称" min-width="120" show-overflow-tooltip />
          <el-table-column prop="comment" label="中文注释" min-width="110" show-overflow-tooltip>
            <template #default="{ row }"><span style="color:#606266">{{ row.comment || '—' }}</span></template>
          </el-table-column>
          <el-table-column prop="dataType" label="类型" width="110" show-overflow-tooltip />
          <el-table-column prop="defaultValue" label="默认值" width="90" show-overflow-tooltip>
            <template #default="{ row }"><span style="color:#909399">{{ row.defaultValue ?? '—' }}</span></template>
          </el-table-column>
          <el-table-column label="必填" width="60" align="center">
            <template #default="{ row }"><el-tag size="small" :type="row.isNullable === false ? 'danger' : 'info'">{{ row.isNullable === false ? '是' : '否' }}</el-tag></template>
          </el-table-column>
        </el-table>

        <!-- 视图：字段名称/中文注释/类型 -->
        <el-table v-else-if="tab === 'views'" :data="columns" size="small" border max-height="380" v-loading="panelLoading">
          <el-table-column prop="name" label="字段名称" min-width="140" show-overflow-tooltip />
          <el-table-column prop="comment" label="中文注释" min-width="140" show-overflow-tooltip>
            <template #default="{ row }"><span style="color:#606266">{{ row.comment || '—' }}</span></template>
          </el-table-column>
          <el-table-column prop="dataType" label="类型" width="130" show-overflow-tooltip />
        </el-table>

        <!-- 存储过程：参数列表（当前值可编辑） -->
        <el-table v-else :data="procParams" size="small" border max-height="380" v-loading="panelLoading">
          <el-table-column prop="name" label="参数名" min-width="110" show-overflow-tooltip />
          <el-table-column prop="dataType" label="参数类型" width="100" show-overflow-tooltip />
          <el-table-column prop="mode" label="模式" width="70" align="center">
            <template #default="{ row }"><el-tag size="small" :type="(row.mode || 'IN').toUpperCase().includes('OUT') ? 'warning' : 'primary'">{{ row.mode || 'IN' }}</el-tag></template>
          </el-table-column>
          <el-table-column prop="defaultValue" label="默认值" width="80" show-overflow-tooltip>
            <template #default="{ row }"><span style="color:#909399">{{ row.defaultValue ?? '—' }}</span></template>
          </el-table-column>
          <el-table-column label="当前值" min-width="140">
            <template #default="{ row }">
              <el-input v-model="row.currentValue" size="small" placeholder="如: 1 或 '张三'" :disabled="(row.mode || '').toUpperCase().includes('OUT')" />
            </template>
          </el-table-column>
        </el-table>
        <div v-if="tab === 'procedures'" class="ch-note" style="margin-top:6px">
          当前值填实参值（字符串请带引号），留空生成 NULL；OUT/INOUT 参数不参与调用。
        </div>
        <el-empty v-if="tab !== 'procedures' && !panelLoading && columns.length === 0"
          description="点击左侧表/视图查看字段" :image-size="40" />
      </div>
    </div>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { ElMessage } from 'element-plus'
import request from '../utils/request'

const props = defineProps<{
  visible: boolean
  dataSourceName: string
  dataSourceType: string
}>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void; (e: 'generated', sql: string): void }>()

function onVisibleChange(v: boolean) { emit('update:visible', v) }

const tab = ref('tables')
const search = ref('')
const loading = ref(false)
const panelLoading = ref(false)
const tables = ref<any[]>([])
const views = ref<any[]>([])
const procedures = ref<any[]>([])
const selectedObject = ref('')
const columns = ref<any[]>([])
const procParams = ref<any[]>([])

const filteredObjects = computed(() => {
  const list = tab.value === 'tables' ? tables.value : tab.value === 'views' ? views.value : procedures.value
  const kw = search.value.trim().toLowerCase()
  return kw ? list.filter((o: any) => (o.name || '').toLowerCase().includes(kw)) : list
})

/** 按名称去重（不区分大小写），防止重复加载或多 schema 同名导致列表重复显示 */
function dedup(list: any[]): any[] {
  const seen = new Set<string>()
  return (list || []).filter((o: any) => {
    const key = (o.name || '').toLowerCase()
    if (!key || seen.has(key)) return false
    seen.add(key)
    return true
  })
}

// 加载序号守卫：只应用最新一次请求的结果（防止慢网络下旧响应覆盖新数据）
let loadSeq = 0

watch(() => props.visible, async v => {
  if (!v) return
  tab.value = 'tables'
  search.value = ''
  selectedObject.value = ''
  columns.value = []
  procParams.value = []
  tables.value = []; views.value = []; procedures.value = []
  loading.value = true
  const seq = ++loadSeq
  try {
    const res: any = await request.post('/system/datasource/metadata', { dataSourceName: props.dataSourceName })
    if (seq !== loadSeq) return   // 已有更新的加载，丢弃本次结果
    tables.value = dedup(res.data?.tables || [])
    views.value = dedup(res.data?.views || [])
    procedures.value = dedup(res.data?.procedures || [])
  } catch { /* 拦截器已提示 */ } finally {
    if (seq === loadSeq) loading.value = false
  }
})

function onTabChange() {
  selectedObject.value = ''
  columns.value = []
  procParams.value = []
}

async function selectObject(name: string) {
  selectedObject.value = name
  if (tab.value === 'procedures') { await loadProcParams(name); return }
  await loadColumns(name)
}

async function loadColumns(name: string) {
  panelLoading.value = true
  columns.value = []
  try {
    const res: any = await request.post('/system/datasource/columns', {
      dataSourceName: props.dataSourceName, tableName: name
    })
    columns.value = res.data || []
  } catch { columns.value = [] } finally { panelLoading.value = false }
}

async function loadProcParams(name: string) {
  panelLoading.value = true
  procParams.value = []
  try {
    const res: any = await request.post('/system/datasource/procedure-params', {
      dataSourceName: props.dataSourceName, procName: name
    })
    procParams.value = (res.data || []).map((p: any) => ({ ...p, currentValue: p.defaultValue || '' }))
  } catch { procParams.value = [] } finally { panelLoading.value = false }
}

function generateCallFromSelected() {
  const proc = procedures.value.find((p: any) => p.name === selectedObject.value)
  if (!proc) { ElMessage.warning('请先在左侧选择存储过程'); return }
  generateFromObject(proc)
}

async function generateFromObject(obj: any) {
  selectedObject.value = obj.name
  const dsType = (props.dataSourceType || 'mysql').toLowerCase()
  let sql = ''
  if (tab.value === 'procedures') {
    if (!procParams.value.length) await loadProcParams(obj.name)
    const args = procParams.value
      .filter((p: any) => !(p.mode || '').toUpperCase().includes('OUT'))
      .map((p: any) => formatCallArg(p.currentValue))
    sql = dsType === 'sqlserver' || dsType === 'mssql' ? `EXEC ${obj.name} ${args.join(', ')}`
      : dsType === 'oracle' || dsType === 'dm' ? `BEGIN ${obj.name}(${args.join(', ')}); END;`
      : `CALL ${obj.name}(${args.join(', ')})`
  } else {
    await loadColumns(obj.name)
    const cols = columns.value.length ? columns.value.map((c: any) => c.name).join(', ') : '*'
    const from = `FROM ${obj.name}`
    if (dsType === 'sqlserver' || dsType === 'mssql') sql = `SELECT TOP 100 ${cols}\n${from}`
    else if (dsType === 'oracle' || dsType === 'dm') sql = `SELECT ${cols}\n${from}\nWHERE ROWNUM <= 100`
    else sql = `SELECT ${cols}\n${from}\nLIMIT 100`
  }
  emit('generated', sql)
  emit('update:visible', false)
  ElMessage.success('已生成 SQL')
}

function formatCallArg(v: string): string {
  const val = (v || '').trim()
  if (!val) return 'NULL'
  if (/^-?\d+(\.\d+)?$/.test(val) || val.toUpperCase() === 'NULL') return val
  return "'" + val.replace(/'/g, "''") + "'"
}
</script>

<style scoped>
.db-object-list { max-height: 400px; overflow-y: auto; border: 1px solid #e4e7ed; border-radius: 6px; }
.db-object-item { display: flex; align-items: center; justify-content: space-between; padding: 6px 10px; cursor: pointer; border-bottom: 1px solid #f0f2f5; }
.db-object-item:last-child { border-bottom: none; }
.db-object-item:hover { background: #f5f7fa; }
.db-object-item.active { background: #ecf5ff; }
.db-object-name { font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ch-note { color: #909399; font-size: 12px; }
.prop-section-title { font-weight: 600; margin: 12px 0 4px; color: #303133; display: flex; align-items: center; }
</style>
