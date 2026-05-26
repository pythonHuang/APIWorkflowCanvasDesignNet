<template>
  <div class="designer-container" @keydown.ctrl.83.prevent="saveReport">
    <div class="toolbar">
      <div style="display:flex;align-items:center;gap:8px">
        <el-button icon="ArrowLeft" link @click="router.back()" style="color:#fff">返回</el-button>
        <el-input v-model="form.name" placeholder="报表名称" size="small" style="width:160px" />
        <el-input v-model="form.groupName" placeholder="分组" size="small" style="width:100px" />
      </div>
      <div style="display:flex;gap:8px;align-items:center">
        <el-select v-model="form.sourceType" size="small" style="width:110px" @change="onSourceTypeChange">
          <el-option value="dataview" label="数据视图" />
          <el-option value="sql" label="自定义SQL" />
          <el-option value="flow" label="流程" />
          <el-option value="api" label="接口" />
        </el-select>
        <el-select v-if="form.sourceType==='dataview'" v-model="form.sourceRef" size="small" style="width:180px" placeholder="选择数据视图">
          <el-option v-for="dv in dvList" :key="dv.id" :label="dv.name" :value="String(dv.id)" />
        </el-select>
        <el-button size="small" @click="loadFields">字段</el-button>
        <el-button size="small" @click="pageSettingsVisible=true" icon="Setting">页面</el-button>
        <el-button size="small" type="primary" @click="saveReport">保存</el-button>
        <el-button size="small" type="success" @click="openPreview">预览</el-button>
      </div>
    </div>
    <div class="designer-body">
      <div class="left-panel">
        <h4 style="margin:0 0 8px">数据字段</h4>
        <div v-if="fields.length===0" style="color:#aaa;font-size:12px">选择数据源后加载字段</div>
        <div v-for="f in fields" :key="f" class="field-item" draggable="true" @dragstart="onDragField($event,f)">{{ f }}</div>
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
                  @mousedown="onCellMouseDown($event, r-1, c-1)"
                  @dblclick="onCellDoubleClick(r-1,c-1)"
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
          <el-input v-model="cellValue" type="textarea" :rows="3" size="small" @change="updateCellValue" placeholder="文本 / ${fieldName} / =SUM(A1:A10)" />
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
import request from '../../utils/request'

const route = useRoute()
const router = useRouter()
const rptId = Number(route.params.id) || 0

const form = reactive({ id:0, name:'', groupName:'', sourceType:'dataview', sourceRef:'', customSql:'', paramsConfig:'[]', layoutJson:'{}', status:1 })
const dvList = ref<any[]>([])
const fields = ref<string[]>([])
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
  loadDvList(); if (rptId>0) loadReport()
  resizeGrid()
})

async function loadDvList() {
  try { const res = await request.post('/report/dataview/page', { pageNum:1, pageSize:200 }); dvList.value = res.data?.list||[] } catch {}
}

async function loadReport() {
  try { const res = await request.post('/report/page', { pageNum:1, pageSize:200 })
    const rpt = (res.data?.list||[]).find((r:any)=>r.id===rptId)
    if (rpt) { Object.assign(form, rpt); loadLayoutJson(); loadFields() }
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

function onCellDoubleClick(r:number,c:number) { selR.value=r; selC.value=c; cellValue.value=getCell(r,c)?.value||'' }

function selectRow(r:number, _e:MouseEvent) {
  if (ctrlDown.value) { if (selectedRows.value.has(r)) selectedRows.value.delete(r); else selectedRows.value.add(r) }
  else { selectedRows.value = new Set([r]); selectedCols.value.clear(); selectedCells.value=[]; selR.value=-1; selC.value=-1 }
  for (let c=0;c<maxCols.value;c++) { const cell=getCell(r,c); boldActive.value=cell?.style?.bold||false }
}

function selectCol(c:number, _e:MouseEvent) {
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

function onDragField(e:DragEvent, field:string) { e.dataTransfer?.setData('field',field) }
function onCellDrop(e:DragEvent, r:number, c:number) { const field = e.dataTransfer?.getData('field'); if (field) { const key=`${r},${c}`,ex=cells.value[key]||{}; cells.value[key]={...ex,value:`\${${field}}`} } }

function onGridScroll() {}

async function loadFields() {
  if (form.sourceType==='dataview' && form.sourceRef) {
    try { const dv=dvList.value.find(d=>String(d.id)===form.sourceRef)
      if (dv) { try { previewParams.value=JSON.parse(dv.parameters||'[]') } catch { previewParams.value=[] } }
      const res = await request.post('/report/dataview/preview',{id:Number(form.sourceRef),params:{}}); fields.value=res.data?.columns||[] }
    catch { fields.value=[] }
  }
}

function onSourceTypeChange() { form.sourceRef=''; fields.value=[] }

function saveLayoutJson() {
  const rows=[]; for (let r=0;r<maxRows.value;r++) rows.push({height:typeof rowHeights.value[r]==='string'?25:(rowHeights.value[r]||25),type:rowHeights.value[r]==='data'?'data':'header'})
  const cols=[]; for (let c=0;c<maxCols.value;c++) cols.push({width:colWidths.value[c]||100})
  const cellArr=Object.entries(cells.value).map(([k,v]:any)=>({r:Number(k.split(',')[0]),c:Number(k.split(',')[1]),...v}))
  form.layoutJson = JSON.stringify({
    page:{size:pageSize.value,orientation:pageOrientation.value,margin:{...pageMargin},header:pageHeader.value,footer:pageFooter.value},
    params:previewParams.value, rows, cols, cells:cellArr
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
.left-panel{width:170px;border-right:1px solid #ddd;padding:8px;overflow-y:auto;background:#fff;flex-shrink:0}
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
