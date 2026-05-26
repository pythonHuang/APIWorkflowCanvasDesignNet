<template>
  <div class="designer-container">
    <div class="toolbar">
      <el-button icon="ArrowLeft" link @click="router.back()" style="color:#fff">返回</el-button>
      <span class="rpt-title">{{ form.name || '新建报表' }}</span>
      <div style="display:flex;gap:8px;align-items:center">
        <el-select v-model="form.sourceType" size="small" style="width:120px" @change="onSourceTypeChange">
          <el-option value="dataview" label="数据视图" />
          <el-option value="sql" label="自定义SQL" />
          <el-option value="flow" label="流程" />
          <el-option value="api" label="接口" />
        </el-select>
        <el-select v-if="form.sourceType==='dataview'" v-model="form.sourceRef" size="small" style="width:180px" placeholder="选择数据视图">
          <el-option v-for="dv in dvList" :key="dv.id" :label="dv.name" :value="String(dv.id)" />
        </el-select>
        <el-button size="small" @click="loadFields">加载字段</el-button>
        <el-button size="small" type="primary" @click="saveReport">保存</el-button>
        <el-button size="small" type="success" @click="showPreview=true;doRender()">预览</el-button>
      </div>
    </div>
    <div class="designer-body">
      <div class="left-panel">
        <h4>数据字段</h4>
        <div v-for="f in fields" :key="f" class="field-item" draggable="true" @dragstart="onDragField($event, f)">{{ f }}</div>
        <el-empty v-if="fields.length===0" description="请先选择数据源并加载字段" />
      </div>
      <div class="center-panel">
        <!-- 样式工具栏 -->
        <div class="style-toolbar">
          <el-button-group size="small">
            <el-button :type="boldActive?'primary':''" @click="applyStyle('bold')"><b>B</b></el-button>
            <el-button :type="italicActive?'primary':''" @click="applyStyle('italic')"><i>I</i></el-button>
          </el-button-group>
          <el-select v-model="selFontSize" size="small" style="width:70px" @change="applyStyle('fontSize')">
            <el-option v-for="s in [10,12,14,16,18,20,24,28]" :key="s" :label="s" :value="s" />
          </el-select>
          <el-color-picker v-model="selColor" size="small" @change="applyStyle('color')" />
          <el-color-picker v-model="selBgColor" size="small" @change="applyStyle('bgColor')" />
          <el-button-group size="small">
            <el-button @click="applyStyle('align','left')">左对齐</el-button>
            <el-button @click="applyStyle('align','center')">居中</el-button>
            <el-button @click="applyStyle('align','right')">右对齐</el-button>
          </el-button-group>
          <el-button size="small" @click="applyStyle('border')">边框</el-button>
          <el-button size="small" @click="mergeSelection">合并</el-button>
          <el-button size="small" @click="splitSelection">拆分</el-button>
        </div>
        <!-- 设计网格 -->
        <div style="flex:1;overflow:auto;padding:8px;background:#e8e8e8">
          <div style="margin-bottom:8px;display:flex;gap:8px;align-items:center">
            <span style="font-size:12px">行列:</span>
            <el-input-number v-model="maxRows" :min="1" :max="100" size="small" style="width:70px" @change="resizeGrid" /><span style="font-size:12px">x</span>
            <el-input-number v-model="maxCols" :min="1" :max="30" size="small" style="width:70px" @change="resizeGrid" />
            <span style="font-size:12px;margin-left:12px">页面:</span>
            <el-select v-model="pageSize" size="small" style="width:80px">
              <el-option v-for="s in ['A4','A3','Letter']" :key="s" :label="s" :value="s" />
            </el-select>
            <el-select v-model="pageOrientation" size="small" style="width:80px">
              <el-option value="portrait" label="纵向" />
              <el-option value="landscape" label="横向" />
            </el-select>
          </div>
          <table class="rpt-grid" @mousedown="onCellMouseDown" @mouseup="onCellMouseUp" @dblclick="onCellDoubleClick">
            <colgroup>
              <col v-for="c in maxCols" :key="c" :style="{ width: colWidths[c-1]||100 }" />
            </colgroup>
            <tr v-for="r in maxRows" :key="r" :style="{ height: rowHeights[r-1]||25 + 'px' }">
              <td v-for="c in maxCols" :key="c"
                :data-r="r-1" :data-c="c-1"
                :class="['rpt-cell', { 'rpt-selected': isSelected(r-1,c-1), 'rpt-active': selR===r-1 && selC===c-1 }]"
                :colspan="mergedSpan(r-1,c-1).colspan"
                :rowspan="mergedSpan(r-1,c-1).rowspan"
                :style="getCellStyle(r-1,c-1)"
                @drop="onCellDrop($event, r-1, c-1)"
                @dragover.prevent
              >{{ getCellText(r-1,c-1) }}</td>
            </tr>
          </table>
        </div>
      </div>
      <div class="right-panel" v-if="selR>=0 && selC>=0">
        <h4>单元格属性</h4>
        <p style="font-size:12px">位置: {{ colLetter(selC) }}{{ selR+1 }}</p>
        <p style="font-size:12px">值:</p>
        <el-input v-model="cellValue" type="textarea" :rows="3" size="small" @change="updateCellValue" placeholder="静态文本 / ${field} / =SUM(A1:A10)" />
        <p style="font-size:12px;margin-top:8px">类型:</p>
        <el-select v-model="rowType" size="small" style="width:100%" @change="updateRowType">
          <el-option value="title" label="标题行" />
          <el-option value="header" label="表头行" />
          <el-option value="data" label="数据行" />
          <el-option value="footer" label="汇总行" />
        </el-select>
      </div>
    </div>

    <el-dialog v-model="showPreview" title="预览" width="90%" top="5vh" fullscreen>
      <div v-if="previewParams.length>0" style="display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap">
        <el-input v-for="p in previewParams" :key="p.name" v-model="previewValues[p.name]" size="small" style="width:160px" :placeholder="p.label||p.name" />
        <el-button size="small" type="primary" @click="doRender">查询</el-button>
        <el-button size="small" @click="doPrint">打印</el-button>
      </div>
      <div v-if="previewHtml" v-html="previewHtml" style="border:1px solid #eee;padding:16px;overflow:auto;max-height:70vh"></div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'

const route = useRoute()
const router = useRouter()
const rptId = Number(route.params.id) || 0

const form = reactive({ id:0, name:'', groupName:'', sourceType:'dataview', sourceRef:'', customSql:'', paramsConfig:'[]', layoutJson:'{}', status:1 })
const dvList = ref<any[]>([])
const fields = ref<string[]>([])
const maxRows = ref(10)
const maxCols = ref(6)
const rowHeights = ref<(number|string)[]>([])
const colWidths = ref<(number|string)[]>([])
const pageSize = ref('A4')
const pageOrientation = ref('portrait')
const cells = ref<Record<string,any>>({}) // "r,c" → {value,style,colspan,rowspan}
const selR = ref(-1), selC = ref(-1)
const selStart = ref<[number,number]|null>(null) // drag selection
const boldActive = ref(false), italicActive = ref(false)
const selFontSize = ref(12), selColor = ref(''), selBgColor = ref('')
const cellValue = ref('')
const rowType = ref('data')
const showPreview = ref(false)
const previewHtml = ref('')
const previewParams = ref<any[]>([])
const previewValues = ref<Record<string,any>>({})

onMounted(async () => {
  try { const res = await request.post('/report/dataview/page', { pageNum:1, pageSize:200 })
    dvList.value = res.data?.list || [] } catch {}
  if (rptId > 0) {
    try { const res = await request.post('/report/page', { pageNum:1, pageSize:1 })
      const rpt = (res.data?.list||[]).find((r:any)=>r.id===rptId)
      if (rpt) {
        Object.assign(form, rpt)
        loadLayoutJson()
        loadFields()
      }
    } catch {}
  }
  resizeGrid()
})

function loadLayoutJson() {
  try {
    const layout = JSON.parse(form.layoutJson)
    maxRows.value = layout.rows?.length || 10
    maxCols.value = layout.cols?.length || 6
    rowHeights.value = layout.rows?.map((r:any)=>r.height||25) || []
    colWidths.value = layout.cols?.map((c:any)=>c.width||100) || []
    pageSize.value = layout.page?.size || 'A4'
    pageOrientation.value = layout.page?.orientation || 'portrait'
    cells.value = {}
    if (layout.cells) for (const c of layout.cells) cells.value[`${c.r},${c.c}`] = c
    previewParams.value = layout.params || []
  } catch {}
}

function resizeGrid() {
  while (rowHeights.value.length < maxRows.value) rowHeights.value.push(25)
  while (colWidths.value.length < maxCols.value) colWidths.value.push(100)
}

function getCell(r: number, c: number) { return cells.value[`${r},${c}`] }
function getCellText(r: number, c: number) { return getCell(r,c)?.value || '' }
function getCellStyle(r: number, c: number) {
  const s = getCell(r,c)?.style || {}
  let style = ''
  if (s.bold) { style += 'font-weight:bold;'; boldActive.value = true } else boldActive.value = false
  if (s.italic) style += 'font-style:italic;'
  if (s.fontSize) style += `font-size:${s.fontSize}px;`
  if (s.color) style += `color:${s.color};`
  if (s.bgColor) style += `background-color:${s.bgColor};`
  if (s.align) style += `text-align:${s.align};`
  if (s.border === false) style += 'border:none;'
  return style
}

function isSelected(r: number, c: number) {
  if (!selStart.value || selR.value<0 || selC.value<0) return false
  const [sr, sc] = selStart.value
  const r1=Math.min(sr,selR.value), r2=Math.max(sr,selR.value)
  const c1=Math.min(sc,selC.value), c2=Math.max(sc,selC.value)
  return r>=r1 && r<=r2 && c>=c1 && c<=c2
}

function mergedSpan(r: number, c: number) {
  const cell = getCell(r,c)
  return { colspan: cell?.colspan||1, rowspan: cell?.rowspan||1 }
}

function onCellMouseDown(e: MouseEvent) {
  const td = (e.target as HTMLElement).closest('td')
  if (!td) return
  selR.value = Number(td.dataset.r)
  selC.value = Number(td.dataset.c)
  selStart.value = [selR.value, selC.value]
  const cell = getCell(selR.value, selC.value)
  cellValue.value = cell?.value || ''
  rowType.value = rowHeights.value[selR.value]?.toString() || 'data'
}

function onCellMouseUp() {
  if (selR.value>=0 && selC.value>=0) {
    const cell = getCell(selR.value, selC.value)
    boldActive.value = cell?.style?.bold || false
    italicActive.value = cell?.style?.italic || false
    selFontSize.value = cell?.style?.fontSize || 12
    selColor.value = cell?.style?.color || ''
    selBgColor.value = cell?.style?.bgColor || ''
  }
}

function onCellDoubleClick(e: MouseEvent) {
  const td = (e.target as HTMLElement).closest('td')
  if (!td) return
  selR.value = Number(td.dataset.r); selC.value = Number(td.dataset.c)
  const val = getCell(selR.value, selC.value)?.value || ''
  cellValue.value = val
}

function updateCellValue() {
  if (selR.value<0||selC.value<0) return
  const key = `${selR.value},${selC.value}`
  const existing = cells.value[key] || {}
  cells.value[key] = { ...existing, value: cellValue.value }
}

function updateRowType() {
  if (selR.value<0) return
  rowHeights.value[selR.value] = rowType.value === 'title' ? 35 : rowType.value === 'header' ? 28 : 25
}

function applyStyle(prop: string, val?: any) {
  if (selR.value<0||selC.value<0||!selStart.value) return
  const [sr,sc] = selStart.value
  for (let r=Math.min(sr,selR.value); r<=Math.max(sr,selR.value); r++)
    for (let c=Math.min(sc,selC.value); c<=Math.max(sc,selC.value); c++) {
      const key = `${r},${c}`; const existing = cells.value[key] || { value:'' }
      const style = existing.style || {}
      if (prop==='border') style.border = style.border===false ? true : !style.border
      else if (prop==='bold') style.bold = val!==undefined ? val : !style.bold
      else if (prop==='italic') style.italic = val!==undefined ? val : !style.italic
      else style[prop] = val
      cells.value[key] = { ...existing, style }
    }
}

function mergeSelection() {
  if (!selStart.value) return
  const [sr,sc] = selStart.value
  const r1=Math.min(sr,selR.value), r2=Math.max(sr,selR.value)
  const c1=Math.min(sc,selC.value), c2=Math.max(sc,selC.value)
  if (r1===r2 && c1===c2) return
  const key = `${r1},${c1}`; const existing = cells.value[key] || { value:'' }
  cells.value[key] = { ...existing, colspan: c2-c1+1, rowspan: r2-r1+1 }
}

function splitSelection() {
  if (selR.value<0||selC.value<0) return
  const key = `${selR.value},${selC.value}`; const existing = cells.value[key]
  if (existing) { delete existing.colspan; delete existing.rowspan; cells.value[key] = { ...existing } }
}

function onDragField(e: DragEvent, field: string) { e.dataTransfer?.setData('field', field) }
function onCellDrop(e: DragEvent, r: number, c: number) {
  const field = e.dataTransfer?.getData('field')
  if (!field) return
  const key = `${r},${c}`; const existing = cells.value[key] || {}
  cells.value[key] = { ...existing, value: `\${${field}}` }
}

async function loadFields() {
  if (form.sourceType==='dataview' && form.sourceRef) {
    try {
      const res = await request.post('/report/dataview/preview', { id: Number(form.sourceRef), params: {} })
      fields.value = res.data?.columns || []
    } catch { fields.value = [] }
  }
}

function onSourceTypeChange() { form.sourceRef = ''; fields.value = [] }

function saveLayoutJson() {
  const rows = []
  for (let r=0; r<maxRows.value; r++) rows.push({ height: typeof rowHeights.value[r]==='string'?25:(rowHeights.value[r]||25), type: typeof rowHeights.value[r]==='string'?rowHeights.value[r]:'data' })
  const cols = []; for (let c=0; c<maxCols.value; c++) cols.push({ width: colWidths.value[c]||100 })
  const cellArr = Object.entries(cells.value).map(([k,v]:any)=>({ r:Number(k.split(',')[0]), c:Number(k.split(',')[1]), ...v }))
  form.layoutJson = JSON.stringify({
    page: { size: pageSize.value, orientation: pageOrientation.value, margin: { top:20,right:15,bottom:20,left:15 } },
    params: previewParams.value, rows, cols, cells: cellArr
  })
}

async function saveReport() {
  saveLayoutJson()
  if (rptId>0) await request.put('/report/update', { ...form, id: rptId })
  else { const res = await request.post('/report/add', form); form.id = res.data }
  ElMessage.success('保存成功')
}

async function doRender() {
  saveLayoutJson(); await saveReport()
  saveLayoutJson()
  try {
    const res = await request.post('/report/preview', { id: form.id, params: previewValues.value })
    previewHtml.value = res.data?.html || ''
  } catch (e) { ElMessage.error('预览失败') }
}

function doPrint() { window.print() }
function colLetter(n: number) { return String.fromCharCode(65+n) }
</script>

<style scoped>
.designer-container { height:100vh;display:flex;flex-direction:column;background:#f5f5f5 }
.toolbar { display:flex;justify-content:space-between;align-items:center;padding:8px 16px;background:#001529;color:#fff;flex-shrink:0 }
.rpt-title { font-size:16px;font-weight:600 }
.designer-body { flex:1;display:flex;overflow:hidden }
.left-panel { width:180px;border-right:1px solid #ddd;padding:8px;overflow-y:auto;background:#fff;flex-shrink:0 }
.center-panel { flex:1;display:flex;flex-direction:column;overflow:hidden }
.style-toolbar { display:flex;gap:4px;padding:6px 8px;background:#fff;border-bottom:1px solid #ddd;flex-wrap:wrap;flex-shrink:0 }
.right-panel { width:220px;border-left:1px solid #ddd;padding:8px;overflow-y:auto;background:#fff;flex-shrink:0 }
.field-item { padding:4px 8px;margin-bottom:2px;background:#e6f7ff;border-radius:4px;cursor:grab;font-size:12px }
.field-item:hover { background:#bae7ff }
.rpt-grid { border-collapse:collapse;background:#fff;font-size:12px }
.rpt-cell { border:1px solid #d9d9d9;padding:2px 4px;min-width:40px;cursor:cell;position:relative }
.rpt-cell:hover { background:#e6f7ff }
.rpt-selected { background:#bae7ff }
.rpt-active { outline:2px solid #1890ff;outline-offset:-2px }
</style>
