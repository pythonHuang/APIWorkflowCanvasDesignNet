<template>
  <div class="designer-container" @keydown.ctrl.83.prevent="saveReport">
    <div class="toolbar">
      <div style="display:flex;align-items:center;gap:8px">
        <el-button icon="ArrowLeft" link @click="router.back()" style="color:#fff">返回</el-button>
        <el-input v-model="form.name" placeholder="报表名称" size="small" style="width:160px" />
        <el-input v-model="form.groupName" placeholder="分组" size="small" style="width:100px" />
      </div>
      <div style="display:flex;gap:8px;align-items:center">
        <span style="font-size:11px;color:#aaa">{{ datasets.length }} 个数据集</span>
        <el-button size="small" @click="pageSettingsVisible=true" icon="Setting">页面</el-button>
        <el-button size="small" type="primary" @click="saveReport">保存</el-button>
        <el-button size="small" type="success" @click="openPreview">预览</el-button>
      </div>
    </div>
    <div class="designer-body">
      <div class="left-panel">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px">
          <h4 style="margin:0">数据集</h4>
          <el-button size="small" type="primary" icon="Plus" circle @click="openDsDialog" />
        </div>
        <div v-if="datasets.length===0" style="color:#aaa;font-size:12px">点击 + 添加数据集</div>
        <div v-for="(ds, di) in datasets" :key="ds.id" style="margin-bottom:4px;border:1px solid #e8e8e8;border-radius:4px;overflow:hidden;background:#fff">
          <div style="display:flex;align-items:center;gap:2px;padding:2px 4px;background:#f5f5f5;font-size:12px">
            <span style="font-weight:600;cursor:pointer;flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" @click="ds.expanded=!ds.expanded">{{ ds.expanded?'▼':'▶' }} {{ ds.name }}</span>
            <el-tag size="small" type="info" style="flex-shrink:0;margin-right:2px">{{ sourceLabel(ds.sourceType) }}</el-tag>
            <span style="display:flex;gap:0;flex-shrink:0">
              <el-button size="small" link @click="openDsEdit(di)" title="编辑"><el-icon :size="14"><Edit /></el-icon></el-button>
              <el-button v-if="ds.fields.length>0" size="small" link @click="previewDsData(di)" title="预览"><el-icon :size="14"><View /></el-icon></el-button>
              <el-button size="small" link @click="loadDsFields(di)" title="刷新"><el-icon :size="14"><Refresh /></el-icon></el-button>
              <el-button size="small" link type="danger" @click="datasets.splice(di,1)" title="删除" style="padding:0 2px"><el-icon :size="12"><Close /></el-icon></el-button>
            </span>
          </div>
          <div v-if="ds.expanded" style="padding:2px 4px;max-height:180px;overflow-y:auto;background:#fafafa">
            <div v-if="ds.loading" style="color:#aaa;font-size:11px;text-align:center;padding:8px">加载中...</div>
            <div v-else>
              <div v-for="f in ds.fields" :key="f" class="field-item"
                draggable="true" @dragstart="onDragField($event,f,ds.name)"
                @click="insertField(ds.name, f)">{{ f }}</div>
              <div v-if="ds.fields.length===0" style="color:#aaa;font-size:11px;text-align:center;padding:4px">
                暂无字段，点击 ↻ 刷新
              </div>
            </div>
          </div>
        </div>
      </div>
      <div class="center-panel">
        <div class="style-toolbar">
          <el-select v-model="selFontName" size="small" style="width:110px" @change="applyStyle('fontName')">
            <el-option v-for="f in ['Microsoft YaHei','SimSun','SimHei','Arial','Times New Roman']" :key="f" :label="f" :value="f" />
          </el-select>
          <el-select v-model="selFontSize" size="small" style="width:55px" @change="applyStyle('fontSize')">
            <el-option v-for="s in [8,9,10,11,12,14,16,18,20,24,28,36]" :key="s" :label="String(s)" :value="s" />
          </el-select>
          <el-button-group size="small">
            <el-button :type="boldActive?'primary':''" @click="applyStyle('bold')"><b>B</b></el-button>
            <el-button :type="italicActive?'primary':''" @click="applyStyle('italic')"><i>I</i></el-button>
            <el-button @click="applyStyle('underline')"><u>U</u></el-button>
          </el-button-group>
          <el-color-picker v-model="selColor" size="small" @change="applyStyle('color')" />
          <el-color-picker v-model="selBgColor" size="small" @change="applyStyle('bgColor')" />
          <el-button-group size="small">
            <el-button @click="applyStyle('align','left')">左</el-button>
            <el-button @click="applyStyle('align','center')">中</el-button>
            <el-button @click="applyStyle('align','right')">右</el-button>
          </el-button-group>
          <el-button size="small" @click="toggleBorder">边框</el-button>
          <el-button size="small" @click="mergeSelected" :disabled="!canMerge">合并</el-button>
          <el-button size="small" @click="splitSelected">拆分</el-button>
          <span style="font-size:11px;color:#888;margin-left:4px">Ctrl多选</span>
        </div>
        <div class="grid-wrapper" @scroll="onGridScroll">
          <table class="rpt-grid">
            <colgroup>
              <col class="row-header-col" />
              <col v-for="c in maxCols" :key="c" :style="{ width: (colWidths[c-1]||100)+'px' }" />
            </colgroup>
            <!-- 列头行 -->
            <thead>
              <tr class="col-header-row">
                <th class="corner-cell"></th>
                <th v-for="c in maxCols" :key="c" class="col-header"
                  :class="{ 'col-header-sel': selectedCols.has(c-1) }"
                  @click="selectCol(c-1, $event)"
                  @dblclick="autoFitCol(c-1)">
                  {{ colLetter(c-1) }}
                  <div class="col-resizer" @mousedown.stop="startColResize($event, c-1)"></div>
                </th>
              </tr>
            </thead>
            <!-- 数据行 -->
            <tbody>
              <tr v-for="r in maxRows" :key="r" :style="{ height: (rowHeights[r-1]||25)+'px' }">
                <td class="row-header" :class="{ 'row-header-sel': selectedRows.has(r-1) }"
                  @click="selectRow(r-1, $event)" @dblclick="autoFitRow(r-1)">
                  {{ r }}
                  <div class="row-resizer" @mousedown.stop="startRowResize($event, r-1)"></div>
                </td>
                <td v-for="c in maxCols" :key="c"
                  :data-r="r-1" :data-c="c-1"
                  :class="getCellClasses(r-1,c-1)"
                  :colspan="cellSpan(r-1,c-1).colspan"
                  :rowspan="cellSpan(r-1,c-1).rowspan"
                  :style="getCellStyle(r-1,c-1)"
                  contenteditable="true"
                  @mousedown="onCellMouseDown($event, r-1, c-1)"
                  @input="onCellInput($event, r-1, c-1)"
                  @blur="onCellBlur(r-1, c-1)"
                  @keydown.delete="onCellDelete(r-1, c-1)"
                  @keydown.backspace="onCellDelete(r-1, c-1)"
                  @dragover.prevent
                  @drop="onCellDrop($event, r-1, c-1)"
                >{{ getCellText(r-1,c-1) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
      <div class="right-panel">
        <h4 style="margin:0 0 8px">单元格属性</h4>
        <div v-if="hasSelection" style="font-size:12px">
            <p>位置: {{ hasSelection ? colLetter(selC) + (selR+1) : '' }} {{ selectedCells.length>1 ? `(+${selectedCells.length-1}格)` : '' }}</p>
          <p>值:</p>
          <el-input v-model="cellValue" type="textarea" :rows="3" size="small" @input="updateCellValue" placeholder="文本 / ${fieldName} / =SUM(A1:A10)" />
          <p style="margin:8px 0 4px">类型:</p>
          <el-select v-model="rowType" size="small" style="width:100%" @change="updateRowType">
            <el-option value="title" label="标题行" />
            <el-option value="header" label="表头行" />
            <el-option value="data" label="数据行(扩展)" />
            <el-option value="footer" label="汇总行" />
          </el-select>
        </div>
        <el-empty v-else description="点击单元格查看属性" />
      </div>
    </div>

    <!-- 页面设置对话框 -->
    <el-dialog v-model="pageSettingsVisible" title="页面设置" width="420px">
      <el-form label-width="80px" size="small">
        <el-form-item label="纸张大小">
          <el-select v-model="pageSize" style="width:100%"><el-option v-for="s in ['A4','A3','Letter','Legal']" :key="s" :label="s" :value="s" /></el-select>
        </el-form-item>
        <el-form-item label="方向">
          <el-radio-group v-model="pageOrientation"><el-radio value="portrait">纵向</el-radio><el-radio value="landscape">横向</el-radio></el-radio-group>
        </el-form-item>
        <el-form-item label="页边距(px)"><el-input v-model="pageMargin.top" placeholder="上" style="width:60px" /><span style="margin:0 4px">-</span><el-input v-model="pageMargin.right" placeholder="右" style="width:60px" /><span style="margin:0 4px">-</span><el-input v-model="pageMargin.bottom" placeholder="下" style="width:60px" /><span style="margin:0 4px">-</span><el-input v-model="pageMargin.left" placeholder="左" style="width:60px" /></el-form-item>
        <el-form-item label="页眉"><el-input v-model="pageHeader" placeholder="如: &quot;销售报表 - ${date}&quot;" /></el-form-item>
        <el-form-item label="页脚"><el-input v-model="pageFooter" placeholder="如: &quot;第 ${page} 页 / 共 ${total} 页&quot;" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="pageSettingsVisible=false">确定</el-button></template>
    </el-dialog>

    <!-- 添加数据集对话框 -->
    <el-dialog v-model="dsDialogVisible" title="添加数据集" width="520px">
      <el-form :model="dsForm" label-width="90px" size="small">
        <el-form-item label="数据集名称"><el-input v-model="dsForm.name" placeholder="如: 主数据、子表1" /></el-form-item>
        <el-form-item label="数据来源">
          <el-radio-group v-model="dsForm.sourceType">
            <el-radio value="dataview">数据视图</el-radio>
            <el-radio value="sql">自定义SQL</el-radio>
            <el-radio value="flow">流程</el-radio>
            <el-radio value="api">接口</el-radio>
          </el-radio-group>
        </el-form-item>
        <template v-if="dsForm.sourceType==='dataview'">
          <el-form-item label="选择视图">
            <el-select v-model="dsForm.sourceRef" style="width:100%"><el-option v-for="dv in dvList" :key="dv.id" :label="`${dv.name} (${dv.groupName||''})`" :value="String(dv.id)" /></el-select>
          </el-form-item>
        </template>
        <template v-else-if="dsForm.sourceType==='sql'">
          <el-form-item label="数据源">
            <el-select v-model="dsForm.dataSourceId" style="width:100%" filterable><el-option v-for="ds_ in dsList" :key="ds_.id" :label="`${ds_.dataSourceName} (${ds_.dataSourceType})`" :value="ds_.id" /></el-select>
          </el-form-item>
          <el-form-item label="SQL"><el-input v-model="dsForm.customSql" type="textarea" :rows="3" placeholder="SELECT * FROM t WHERE id=@id" /></el-form-item>
        </template>
        <template v-else-if="dsForm.sourceType==='flow'">
          <el-form-item label="选择流程">
            <el-select v-model="dsForm.sourceRef" style="width:100%" filterable @change="flowSelChange"><el-option v-for="f in flowList" :key="f.flowKey" :label="`${f.flowName}(${f.flowKey})`" :value="f.flowKey" /></el-select>
          </el-form-item>
        </template>
        <template v-else-if="dsForm.sourceType==='api'">
          <el-form-item label="选择接口">
            <el-select v-model="dsForm.sourceRef" style="width:100%" filterable><el-option v-for="a in apiList" :key="a.methodCode" :label="`${a.methodName}(${a.methodCode})`" :value="a.methodCode" /></el-select>
          </el-form-item>
        </template>
      </el-form>
      <template #footer><el-button @click="dsDialogVisible=false">取消</el-button><el-button type="primary" @click="addDataset">添加</el-button></template>
    </el-dialog>

    <!-- 编辑数据集对话框 -->
    <el-dialog v-model="dsEditVisible" title="编辑数据集" width="520px">
      <el-form :model="dsEditForm" label-width="90px" size="small">
        <el-form-item label="名称"><el-input v-model="dsEditForm.name" /></el-form-item>
        <el-form-item label="数据来源"><el-input :value="sourceLabel(dsEditForm.sourceType)" disabled /></el-form-item>
        <template v-if="dsEditForm.sourceType==='dataview'">
          <el-form-item label="选择视图"><el-select v-model="dsEditForm.sourceRef" style="width:100%"><el-option v-for="dv in dvList" :key="dv.id" :label="dv.name" :value="String(dv.id)" /></el-select></el-form-item>
        </template>
        <template v-if="dsEditForm.sourceType==='sql'">
          <el-form-item label="数据源"><el-select v-model="dsEditForm.dataSourceId" style="width:100%"><el-option v-for="ds_ in dsList" :key="ds_.id" :label="ds_.dataSourceName" :value="ds_.id" /></el-select></el-form-item>
          <el-form-item label="SQL"><el-input v-model="dsEditForm.customSql" type="textarea" :rows="4" /></el-form-item>
        </template>
        <template v-if="dsEditForm.sourceType==='flow'">
          <el-form-item label="选择流程"><el-select v-model="dsEditForm.sourceRef" style="width:100%"><el-option v-for="f in flowList" :key="f.flowKey" :label="f.flowName||f.flowKey" :value="f.flowKey" /></el-select></el-form-item>
        </template>
        <template v-if="dsEditForm.sourceType==='api'">
          <el-form-item label="选择接口"><el-select v-model="dsEditForm.sourceRef" style="width:100%"><el-option v-for="a in apiList" :key="a.methodCode" :label="a.methodName||a.methodCode" :value="a.methodCode" /></el-select></el-form-item>
        </template>
      </el-form>
      <template #footer><el-button @click="dsEditVisible=false">取消</el-button><el-button type="primary" @click="saveDsEdit">保存并刷新</el-button></template>
    </el-dialog>

    <!-- 数据预览对话框 -->
    <el-dialog v-model="dsPreviewVisible" title="数据预览" width="80%" top="5vh">
      <div v-if="dsPreviewLoading" style="text-align:center;padding:40px"><el-icon class="is-loading" :size="32"><Loading /></el-icon></div>
      <div v-else>
        <div style="margin-bottom:8px;color:#888;font-size:12px">共 {{ dsPreviewRows.length }} 条</div>
        <el-table :data="dsPreviewRows" border size="small" max-height="400" stripe>
          <el-table-column v-for="col in dsPreviewCols" :key="col" :prop="col" :label="col" show-overflow-tooltip />
        </el-table>
      </div>
    </el-dialog>

    <!-- 预览 -->
    <el-dialog v-model="showPreview" title="预览" width="90%" top="5vh">
      <div v-if="previewParams.length>0" style="display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap">
        <el-input v-for="p in previewParams" :key="p.name" v-model="previewValues[p.name]" size="small" style="width:160px" :placeholder="p.label||p.name" />
        <el-button size="small" type="primary" @click="doRender">查询</el-button>
        <el-button size="small" @click="doPrint">打印</el-button>
      </div>
      <div v-if="previewHtml" v-html="previewHtml" style="border:1px solid #eee;padding:16px;overflow:auto;max-height:65vh"></div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Edit, View, Refresh, Loading, Close } from '@element-plus/icons-vue'
import request from '../../utils/request'

const route = useRoute()
const router = useRouter()
const rptId = Number(route.params.id) || 0

const form = reactive({ id:0, name:'', groupName:'', sourceType:'dataview', sourceRef:'', customSql:'', paramsConfig:'[]', layoutJson:'{}', status:1 })
const dvList = ref<any[]>([]), dsList = ref<any[]>([]), flowList = ref<any[]>([]), apiList = ref<any[]>([])

// 数据集管理
interface Dataset { id: string; name: string; sourceType: string; sourceRef: string; customSql: string; dataSourceId: string|number; fields: string[]; expanded: boolean; loading: boolean }
const datasets = ref<Dataset[]>([])
const dsDialogVisible = ref(false), dsEditVisible = ref(false), dsEditIdx = ref(-1)
const dsForm = reactive({ name:'', sourceType:'dataview', sourceRef:'', customSql:'', dataSourceId:'' })
const dsEditForm = reactive({ name:'', sourceType:'', sourceRef:'', customSql:'', dataSourceId:'' })
const dsPreviewVisible = ref(false), dsPreviewLoading = ref(false)
const dsPreviewCols = ref<string[]>([]), dsPreviewRows = ref<any[]>([])
const maxRows = ref(10), maxCols = ref(6)
const rowHeights = ref<(number|string)[]>([])
const colWidths = ref<(number|string)[]>([])
const pageSize = ref('A4'), pageOrientation = ref('portrait')
const pageMargin = reactive({ top:20, right:15, bottom:20, left:15 })
const pageHeader = ref(''), pageFooter = ref('')
const cells = ref<Record<string,any>>({})
const selR = ref(-1), selC = ref(-1)
const selectedCells = ref<[number,number][]>([]) // [{r,c}]
const selectedRows = ref(new Set<number>())
const selectedCols = ref(new Set<number>())
const ctrlDown = ref(false)
const boldActive = ref(false), italicActive = ref(false)
const selFontSize = ref(12), selFontName = ref('Microsoft YaHei'), selColor = ref(''), selBgColor = ref('')
const cellValue = ref(''), rowType = ref('data')
const showPreview = ref(false), pageSettingsVisible = ref(false)
const previewHtml = ref(''), previewParams = ref<any[]>([])
const previewValues = ref<Record<string,any>>({})

const canMerge = computed(() => selectedCells.value.length >= 2)
const hasSelection = computed(() => selR.value >= 0 && selC.value >= 0)

onMounted(() => {
  document.addEventListener('keydown', (e) => { if (e.key==='Control') ctrlDown.value = true })
  document.addEventListener('keyup', (e) => { if (e.key==='Control') ctrlDown.value = false })
  loadDvList(); loadDsList(); loadFlowList(); loadApiList()
  if (rptId>0) loadReport()
  resizeGrid()
})

async function loadDvList() {
  try { const res = await request.post('/report/dataview/page', { pageNum:1, pageSize:200 }); dvList.value = res.data?.list||[] } catch {}
}
async function loadDsList() {
  try { const res = await request.get('/system/datasource/list'); dsList.value = res.data||[] } catch {}
}
async function loadFlowList() {
  try { const res = await request.post('/flow/definition/page', { pageNum:1, pageSize:200 }); flowList.value = res.data?.records||[] } catch {}
}
async function loadApiList() {
  try { const res = await request.post('/suite/api/list', { suiteCode: '' }); apiList.value = res.data||[] } catch {}
}

function sourceLabel(t: string) { const m: Record<string,string>={dataview:'视图',sql:'SQL',flow:'流程',api:'接口'}; return m[t]||t }
function flowSelChange() {}

function openDsEdit(di: number) {
  dsEditIdx.value = di
  const ds = datasets.value[di]
  Object.assign(dsEditForm, { name: ds.name, sourceType: ds.sourceType, sourceRef: ds.sourceRef, customSql: ds.customSql, dataSourceId: ds.dataSourceId })
  dsEditVisible.value = true
}

function saveDsEdit() {
  if (dsEditIdx.value < 0) return
  const ds = datasets.value[dsEditIdx.value]
  ds.name = dsEditForm.name; ds.sourceRef = dsEditForm.sourceRef; ds.customSql = dsEditForm.customSql; ds.dataSourceId = dsEditForm.dataSourceId
  dsEditVisible.value = false
  loadDsFields(dsEditIdx.value)
}

async function previewDsData(di: number) {
  dsPreviewVisible.value = true; dsPreviewLoading.value = true; dsPreviewCols.value = []; dsPreviewRows.value = []
  const ds = datasets.value[di]
  try {
    switch (ds.sourceType) {
      case 'dataview':
        const res = await request.post('/report/dataview/preview', { id: Number(ds.sourceRef), params: {} })
        dsPreviewCols.value = res.data?.columns||[]; dsPreviewRows.value = res.data?.rows||[]; break
      case 'sql':
        if (ds.dataSourceId && ds.customSql) {
          const r2 = await request.post('/report/dataview/preview', { id: 0, sql: ds.customSql, dataSourceId: Number(ds.dataSourceId), params: {} })
          dsPreviewCols.value = r2.data?.columns||[]; dsPreviewRows.value = r2.data?.rows||[]
        }
        break
    }
  } catch {} finally { dsPreviewLoading.value = false }
}

function openDsDialog() {
  Object.assign(dsForm, { name:'', sourceType:'dataview', sourceRef:'', customSql:'', dataSourceId:'' })
  dsDialogVisible.value = true
}

function addDataset() {
  if (!dsForm.name) { dsForm.name = dsForm.sourceType + '_' + (datasets.value.length+1) }
  const ds: Dataset = { id: Date.now().toString(), name: dsForm.name, sourceType: dsForm.sourceType, sourceRef: dsForm.sourceRef, customSql: dsForm.customSql, dataSourceId: dsForm.dataSourceId, fields: [], expanded: true, loading: false }
  datasets.value.push(ds)
  dsDialogVisible.value = false
  loadDsFields(datasets.value.length - 1)
}

async function loadDsFields(di: number) {
  const ds = datasets.value[di]
  ds.loading = true
  try {
    switch (ds.sourceType) {
      case 'dataview':
        const dv = dvList.value.find(d => String(d.id) === ds.sourceRef)
        if (dv) {
          try { const res = await request.post('/report/dataview/preview', { id: Number(ds.sourceRef), params: {} }); ds.fields = res.data?.columns||[] } catch {}
          const params = JSON.parse(dv.parameters||'[]')
          previewParams.value = [...previewParams.value, ...params.filter((p:any)=>!previewParams.value.find((q:any)=>q.name===p.name))]
        }
        break
      case 'sql':
        if (ds.dataSourceId && ds.customSql) {
          try { const res = await request.post('/report/dataview/preview', { id: 0, sql: ds.customSql, dataSourceId: Number(ds.dataSourceId), params: {} }); ds.fields = res.data?.columns||[] } catch {}
        }
        break
      case 'flow':
        if (ds.sourceRef) {
          try { const res = await request.get(`/flow/definition/output-params/${ds.sourceRef}`); ds.fields = res.data||[] } catch { ds.fields = [] }
        }
        break
      case 'api':
        if (ds.sourceRef) {
          try { const res = await request.get(`/suite/api/output-params/${ds.sourceRef}`); ds.fields = res.data||[] } catch { ds.fields = [] }
        }
        break
    }
  } finally { ds.loading = false }
}

function insertField(dsName: string, field: string) {
  if (selR.value<0||selC.value<0) return
  const key=`${selR.value},${selC.value}`, existing=cells.value[key]||{}
  cells.value[key] = {...existing, value: `\${${dsName}.${field}}`}
  cellValue.value = `\${${dsName}.${field}}`
}

function onDragField(_e:DragEvent, field:string, dsName?:string) {
  const val = dsName ? `\${${dsName}.${field}}` : `\${${field}}`
  _e.dataTransfer?.setData('field', val)
}

async function loadReport() {
  try { const res = await request.post('/report/page', { pageNum:1, pageSize:200 })
    const rpt = (res.data?.list||[]).find((r:any)=>r.id===rptId)
    if (rpt) { Object.assign(form, rpt); loadLayoutJson() }
  } catch {}
}

function loadLayoutJson() {
  try {
    const layout = JSON.parse(form.layoutJson)
    maxRows.value = layout.rows?.length || 10; maxCols.value = layout.cols?.length || 6
    rowHeights.value = layout.rows?.map((r:any)=> r.type==='data'?'data':r.height||25) || []
    colWidths.value = layout.cols?.map((c:any)=>c.width||100) || []
    pageSize.value = layout.page?.size||'A4'; pageOrientation.value = layout.page?.orientation||'portrait'
    if (layout.page?.margin) Object.assign(pageMargin, layout.page.margin)
    pageHeader.value = layout.page?.header||''; pageFooter.value = layout.page?.footer||''
    cells.value = {}; if (layout.cells) for (const c of layout.cells) cells.value[`${c.r},${c.c}`] = c
    previewParams.value = layout.params||[]
    if (layout.datasets) datasets.value = layout.datasets.map((d:any)=>({...d,expanded:true,loading:false}))
  } catch {}
}

function resizeGrid() { while(rowHeights.value.length<maxRows.value) rowHeights.value.push(25); while(colWidths.value.length<maxCols.value) colWidths.value.push(100) }
function getCell(r:number,c:number) { return cells.value[`${r},${c}`] }
function getCellText(r:number,c:number) { return getCell(r,c)?.value||'' }
function cellSpan(r:number,c:number) { return { colspan:getCell(r,c)?.colspan||1, rowspan:getCell(r,c)?.rowspan||1 } }

function getCellStyle(r:number,c:number) {
  const cell = getCell(r,c); const s = cell?.style||{}; let style = ''
  if (s.bold) style+='font-weight:bold;'
  if (s.italic) style+='font-style:italic;'
  if (s.underline) style+='text-decoration:underline;'
  if (s.fontSize) style+=`font-size:${s.fontSize}px;`
  if (s.fontName) style+=`font-family:${s.fontName};`
  if (s.color) style+=`color:${s.color};`
  if (s.bgColor) style+=`background-color:${s.bgColor};`
  if (s.align) style+=`text-align:${s.align};`
  if (s.border===false) style+='border:none;'
  return style
}

function getCellClasses(r:number,c:number) {
  const cls = ['rpt-cell']
  if (selR.value===r && selC.value===c) cls.push('rpt-active')
  if (selectedCells.value.some(([sr,sc])=>sr===r&&sc===c)) cls.push('rpt-selected')
  if (selectedRows.value.has(r)) cls.push('rpt-selected')
  if (selectedCols.value.has(c)) cls.push('rpt-selected')
  return cls
}

function onCellMouseDown(_e:MouseEvent, r:number, c:number) {
  // 先保存上一个单元格的编辑内容
  commitEdit()
  if (ctrlDown.value) {
    if (selectedCells.value.some(([sr,sc])=>sr===r&&sc===c)) selectedCells.value = selectedCells.value.filter(([sr,sc])=>!(sr===r&&sc===c))
    else selectedCells.value.push([r,c])
  } else {
    selR.value = r; selC.value = c
    selectedCells.value = [[r,c]]
    selectedRows.value.clear(); selectedCols.value.clear()
  }
  const cell = getCell(r,c)
  cellValue.value = cell?.value||''
  rowType.value = (rowHeights.value[r]==='data')?'data':'header'
  boldActive.value = cell?.style?.bold||false
  italicActive.value = cell?.style?.italic||false
  selFontSize.value = cell?.style?.fontSize||12
  selColor.value = cell?.style?.color||''; selBgColor.value = cell?.style?.bgColor||''
}

function commitEdit() {
  if (selR.value<0||selC.value<0) return
  const key=`${selR.value},${selC.value}`, existing=cells.value[key]||{}
  if (existing.value !== cellValue.value) {
    cells.value[key]={...existing, value: cellValue.value}
  }
}

function onCellInput(e:Event, r:number, c:number) {
  const text = (e.target as HTMLElement).innerText || ''
  const key=`${r},${c}`, existing=cells.value[key]||{}
  cells.value[key]={...existing, value: text}
  if (selR.value===r && selC.value===c) cellValue.value = text
}
function onCellBlur(_r:number, _c:number) { commitEdit() }
function onCellDelete(r:number, c:number) {
  const key=`${r},${c}`; cells.value[key] = { value: '' }
  if (selR.value===r && selC.value===c) cellValue.value = ''
}

function selectRow(r:number, _e:MouseEvent) {
  commitEdit()
  if (ctrlDown.value) { if (selectedRows.value.has(r)) selectedRows.value.delete(r); else selectedRows.value.add(r) }
  else { selectedRows.value = new Set([r]); selectedCols.value.clear(); selectedCells.value=[]; selR.value=-1; selC.value=-1 }
  for (let c=0;c<maxCols.value;c++) { const cell=getCell(r,c); boldActive.value=cell?.style?.bold||false }
}

function selectCol(c:number, _e:MouseEvent) {
  commitEdit()
  if (ctrlDown.value) { if (selectedCols.value.has(c)) selectedCols.value.delete(c); else selectedCols.value.add(c) }
  else { selectedCols.value = new Set([c]); selectedRows.value.clear(); selectedCells.value=[]; selR.value=-1; selC.value=-1 }
}

function forEachSelected(fn:(r:number,c:number)=>void) {
  const targets = new Set<string>()
  if (selectedRows.value.size>0) for (const r of selectedRows.value) for (let c=0;c<maxCols.value;c++) targets.add(`${r},${c}`)
  else if (selectedCols.value.size>0) for (const c of selectedCols.value) for (let r=0;r<maxRows.value;r++) targets.add(`${r},${c}`)
  else for (const [r,c] of selectedCells.value) targets.add(`${r},${c}`)
  targets.forEach(k => { const [r,c]=k.split(',').map(Number); fn(r,c) })
}

function updateCellValue() {
  if (selR.value<0||selC.value<0) return
  for (const [r,c] of selectedCells.value) {
    const key=`${r},${c}`, existing=cells.value[key]||{}
    cells.value[key]={...existing, value: cellValue.value}
  }
}

function updateRowType() { if (selR.value>=0) rowHeights.value[selR.value]=rowType.value==='title'?35:rowType.value==='header'?28:rowType.value==='data'?'data':25 }

function applyStyle(prop:string, val?:any) {
  forEachSelected((r,c) => {
    const key=`${r},${c}`, existing=cells.value[key]||{value:''}
    const style = existing.style||{}
    if (prop==='bold') { style.bold = val!==undefined?val:!style.bold; boldActive.value=style.bold }
    else if (prop==='italic') { style.italic = val!==undefined?val:!style.italic; italicActive.value=style.italic }
    else if (prop==='underline') { style.underline = val!==undefined?val:!style.underline }
    else { (style as any)[prop] = val }
    cells.value[key] = {...existing, style}
  })
}

function toggleBorder() { forEachSelected((r,c)=>{const key=`${r},${c}`,ex=cells.value[key]||{value:''},s=ex.style||{}; s.border=s.border===false?true:false; cells.value[key]={...ex,style:s}}) }

function mergeSelected() {
  if (selectedCells.value.length<2) return
  const rs = selectedCells.value.map(([r])=>r), cs = selectedCells.value.map(([,c])=>c)
  const r1=Math.min(...rs), r2=Math.max(...rs), c1=Math.min(...cs), c2=Math.max(...cs)
  const base = cells.value[`${r1},${c1}`]||{value:''}
  cells.value[`${r1},${c1}`] = {...base, colspan:c2-c1+1, rowspan:r2-r1+1}
}

function splitSelected() {
  if (selR.value<0||selC.value<0) return
  const key=`${selR.value},${selC.value}`, c=cells.value[key]
  if (c) { delete c.colspan; delete c.rowspan; cells.value[key]={...c} }
}

function startColResize(e:MouseEvent, ci:number) {
  const startX = e.clientX, startW = Number(colWidths.value[ci]||100)
  const onMove = (ev:MouseEvent) => { colWidths.value[ci] = Math.max(40, startW+ev.clientX-startX) }
  const onUp = () => { document.removeEventListener('mousemove',onMove); document.removeEventListener('mouseup',onUp) }
  document.addEventListener('mousemove',onMove); document.addEventListener('mouseup',onUp)
}

function startRowResize(e:MouseEvent, ri:number) {
  const startY = e.clientY, startH = Number(typeof rowHeights.value[ri]==='string'?25:(rowHeights.value[ri]||25))
  const onMove = (ev:MouseEvent) => { rowHeights.value[ri] = Math.max(15, startH+ev.clientY-startY) }
  const onUp = () => { document.removeEventListener('mousemove',onMove); document.removeEventListener('mouseup',onUp) }
  document.addEventListener('mousemove',onMove); document.addEventListener('mouseup',onUp)
}

function autoFitCol(c:number) { colWidths.value[c]=200 }
function autoFitRow(r:number) { rowHeights.value[r]=30 }

function onCellDrop(e:DragEvent, r:number, c:number) { const field = e.dataTransfer?.getData('field'); if (field) { const key=`${r},${c}`,ex=cells.value[key]||{}; cells.value[key]={...ex,value:field} } }

function onGridScroll() {}

function saveLayoutJson() {
  const rows=[]; for (let r=0;r<maxRows.value;r++) rows.push({height:typeof rowHeights.value[r]==='string'?25:(rowHeights.value[r]||25),type:rowHeights.value[r]==='data'?'data':'header'})
  const cols=[]; for (let c=0;c<maxCols.value;c++) cols.push({width:colWidths.value[c]||100})
  const cellArr=Object.entries(cells.value).map(([k,v]:any)=>({r:Number(k.split(',')[0]),c:Number(k.split(',')[1]),...v}))
  const dsArr = datasets.value.map(d=>({id:d.id,name:d.name,sourceType:d.sourceType,sourceRef:d.sourceRef,customSql:d.customSql,dataSourceId:d.dataSourceId,fields:d.fields}))
  form.layoutJson = JSON.stringify({
    page:{size:pageSize.value,orientation:pageOrientation.value,margin:{...pageMargin},header:pageHeader.value,footer:pageFooter.value},
    params:previewParams.value, rows, cols, cells:cellArr,
    datasets: dsArr
  })
}

async function saveReport() { saveLayoutJson()
  if (rptId>0) { await request.put('/report/update',{...form,id:rptId}); ElMessage.success('保存成功') }
  else { const res=await request.post('/report/add',form); form.id=res.data; ElMessage.success('已创建') }
}

function openPreview() { saveLayoutJson(); showPreview.value=true; doRender() }

async function doRender() { try { const res=await request.post('/report/preview',{id:form.id,params:previewValues.value}); previewHtml.value=res.data?.html||'' } catch { ElMessage.error('预览失败') } }

function doPrint() { window.print() }

function colLetter(n:number):string { return String.fromCharCode(65+n) }
</script>

<style scoped>
.designer-container{height:100vh;display:flex;flex-direction:column;background:#f5f5f5}
.toolbar{display:flex;justify-content:space-between;align-items:center;padding:6px 12px;background:#001529;color:#fff;flex-shrink:0}
.designer-body{flex:1;display:flex;overflow:hidden}
.left-panel{width:220px;border-right:1px solid #ddd;padding:6px;overflow-y:auto;background:#fff;flex-shrink:0}
.center-panel{flex:1;display:flex;flex-direction:column;overflow:hidden}
.style-toolbar{display:flex;gap:4px;padding:4px 8px;background:#fff;border-bottom:1px solid #ddd;flex-wrap:wrap;align-items:center;flex-shrink:0}
.right-panel{width:210px;border-left:1px solid #ddd;padding:8px;overflow-y:auto;background:#fff;flex-shrink:0}
.field-item{padding:4px 8px;margin-bottom:2px;background:#e6f7ff;border-radius:4px;cursor:grab;font-size:12px}
.field-item:hover{background:#bae7ff}
.grid-wrapper{flex:1;overflow:auto;padding:8px;background:#e8e8e8}
.rpt-grid{border-collapse:collapse;background:#fff;font-size:12px;table-layout:fixed}
.row-header-col{width:36px}
.corner-cell{width:36px;height:22px;background:#f0f0f0;border-right:1px solid #d9d9d9;border-bottom:1px solid #d9d9d9;position:sticky;top:0;left:0;z-index:3}
.col-header{background:#f0f0f0;border-right:1px solid #d9d9d9;border-bottom:1px solid #d9d9d9;text-align:center;font-size:10px;color:#666;height:22px;position:sticky;top:0;z-index:2;cursor:pointer;user-select:none;position:relative}
.col-header:hover{background:#d9e8ff}
.col-header-sel{background:#b0d0ff!important}
.col-resizer{position:absolute;top:0;right:0;width:4px;height:100%;cursor:col-resize}
.col-resizer:hover{background:#1890ff}
.row-header{background:#f0f0f0;border-right:1px solid #d9d9d9;border-bottom:1px solid #d9d9d9;text-align:center;font-size:10px;color:#666;width:36px;position:sticky;left:0;z-index:1;cursor:pointer;user-select:none;position:relative}
.row-header:hover{background:#d9e8ff}
.row-header-sel{background:#b0d0ff!important}
.row-resizer{position:absolute;bottom:0;left:0;height:4px;width:100%;cursor:row-resize}
.row-resizer:hover{background:#1890ff}
.rpt-cell{border:1px solid #d9d9d9;padding:2px 4px;min-width:40px;cursor:cell;overflow:hidden;white-space:nowrap}
.rpt-cell:hover{background:#e6f7ff}
.rpt-selected{background:#bae7ff!important}
.rpt-active{outline:2px solid #1890ff!important;outline-offset:-2px;z-index:1;position:relative}
</style>
