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
          <el-select v-model="selFontName" size="small" style="width:110px" @change="onStyleChange('fontName')">
            <el-option v-for="f in ['Microsoft YaHei','SimSun','SimHei','Arial','Times New Roman']" :key="f" :label="f" :value="f" />
          </el-select>
          <el-select v-model="selFontSize" size="small" style="width:55px" @change="onStyleChange('fontSize')">
            <el-option v-for="s in [8,9,10,11,12,14,16,18,20,24,28,36]" :key="s" :label="String(s)" :value="s" />
          </el-select>
          <el-button-group size="small">
            <el-button :type="boldActive?'primary':''" @click="applyStyle('bold')"><b>B</b></el-button>
            <el-button :type="italicActive?'primary':''" @click="applyStyle('italic')"><i>I</i></el-button>
            <el-button @click="applyStyle('underline')"><u>U</u></el-button>
          </el-button-group>
          <el-color-picker v-model="selColor" size="small" @change="onStyleChange('color')" />
          <el-color-picker v-model="selBgColor" size="small" @change="onStyleChange('bgColor')" />
          <el-button-group size="small">
            <el-button @click="applyStyle('align','left')">左</el-button>
            <el-button @click="applyStyle('align','center')">中</el-button>
            <el-button @click="applyStyle('align','right')">右</el-button>
          </el-button-group>
          <el-button size="small" @click="toggleBorder">边框</el-button>
          <el-button size="small" @click="mergeSelected" :disabled="!canMerge">合并</el-button>
          <el-button size="small" @click="splitSelected">拆分</el-button>
          <el-button size="small" @click="insertRow(-1)">↑行</el-button>
          <el-button size="small" @click="insertRow(1)">↓行</el-button>
          <el-button size="small" @click="deleteRow">删行</el-button>
          <el-button size="small" @click="insertCol(-1)">←列</el-button>
          <el-button size="small" @click="insertCol(1)">→列</el-button>
          <el-button size="small" @click="deleteCol">删列</el-button>
          <el-button size="small" :disabled="!canUndo" @click="undo" title="Ctrl+Z">↩</el-button>
          <el-button size="small" :disabled="!canRedo" @click="redo" title="Ctrl+Y">↪</el-button>
          <span style="font-size:11px;color:#888;margin-left:4px">Ctrl多选</span>
        </div>
        <div class="grid-wrapper" @scroll="onGridScroll">
          <div class="rpt-page" :style="{ width: pageWidth + 'px', minHeight: pageHeight + 'px' }">
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
          <div class="page-break" v-if="showPageBreak" :style="{ top: pageHeight + 'px' }">
            <span class="page-break-label">—— 分页线 ——</span>
          </div>
          </div>
        </div>
      </div>
      <div class="right-panel">
        <h4 style="margin:0 0 8px">单元格属性</h4>
        <div v-if="hasSelection" style="font-size:12px">
            <p>位置: {{ hasSelection ? colLetter(selC) + (selR+1) : '' }} {{ selectedCells.length>1 ? `(+${selectedCells.length-1}格)` : '' }}</p>
          <div style="display:flex;align-items:center;justify-content:space-between;margin:8px 0 2px">
            <span>值:</span>
            <el-button size="small" icon="QuestionFilled" link title="公式语法帮助" @click="formulaHelpVisible = true">公式帮助</el-button>
          </div>
          <el-input v-model="cellValue" type="textarea" :rows="3" size="small" @input="updateCellValue" placeholder="文本 / ${fieldName} / =SUM(A1:A10)" />
          <p style="margin:8px 0 4px">类型:</p>
          <el-select v-model="rowType" size="small" style="width:100%" @change="updateRowType">
            <el-option value="title" label="标题行" />
            <el-option value="header" label="表头行" />
            <el-option value="data" label="数据行(扩展)" />
            <el-option value="footer" label="汇总行" />
          </el-select>
          <template v-if="rowType==='data' || rowType==='footer'">
            <p style="margin:8px 0 4px">绑定数据集:</p>
            <el-select v-model="rowDataset" size="small" style="width:100%" clearable placeholder="选择数据集" @change="updateRowDataset">
              <el-option v-for="ds in datasets" :key="ds.id" :label="ds.name" :value="ds.id" />
            </el-select>
          </template>
          <div v-if="rowType==='title'" style="margin-top:6px;color:#909399;font-size:11px;line-height:1.6">
            渲染 1 次，用于报表大标题（可合并单元格、调字号）。
          </div>
          <div v-else-if="rowType==='header'" style="margin-top:6px;color:#909399;font-size:11px;line-height:1.6">
            渲染 1 次，用于表格列头（姓名/金额/日期…）。
          </div>
          <div v-else-if="rowType==='data'" style="margin-top:6px;color:#909399;font-size:11px;line-height:1.6">
            绑定数据集后按<b>每条数据自动扩展一行</b>。单元格可写 <code>${字段名}</code>、<code>${数据集.字段}</code>、<code>${rowIndex}</code>（行号）或 <code>=公式</code>。
            <el-checkbox v-model="rowZebra" size="small" @change="updateRowZebra" style="margin-top:4px">斑马纹（预览隔行浅灰底）</el-checkbox>
          </div>
          <div v-else-if="rowType==='footer'" style="margin-top:6px;color:#909399;font-size:11px;line-height:1.6">
            渲染 1 次（数据行下方）。绑定数据集后支持聚合：<code>${金额:SUM}</code>、<code>${字段:AVG}</code>、MIN / MAX / COUNT。
          </div>

          <p style="margin:10px 0 4px">边框:</p>
          <div style="display:flex;gap:4px;flex-wrap:wrap">
            <el-button size="small" :type="bdState.top?'primary':''" @click="setBorderSide('top')">上</el-button>
            <el-button size="small" :type="bdState.bottom?'primary':''" @click="setBorderSide('bottom')">下</el-button>
            <el-button size="small" :type="bdState.left?'primary':''" @click="setBorderSide('left')">左</el-button>
            <el-button size="small" :type="bdState.right?'primary':''" @click="setBorderSide('right')">右</el-button>
            <el-button size="small" @click="setBorderAll(true)">全部</el-button>
            <el-button size="small" @click="setBorderAll(false)">无</el-button>
            <el-button size="small" :type="bdState.diagonal?'primary':''" @click="setBorderDiagonal">交叉</el-button>
          </div>
          <div style="display:flex;gap:6px;margin-top:6px;align-items:center">
            <el-select v-model="bdStyle" size="small" style="width:80px" @change="applyBorderMeta">
              <el-option value="solid" label="实线" />
              <el-option value="dashed" label="虚线" />
              <el-option value="dotted" label="点线" />
              <el-option value="double" label="双线" />
            </el-select>
            <el-select v-model="bdWidth" size="small" style="width:66px" @change="applyBorderMeta">
              <el-option :value="1" label="1px" />
              <el-option :value="2" label="2px" />
              <el-option :value="3" label="3px" />
            </el-select>
            <el-color-picker v-model="bdColor" size="small" @change="applyBorderMeta" />
          </div>

          <p style="margin:10px 0 4px">单元格背景图:</p>
          <el-input v-model="bgImage" size="small" placeholder="图片 URL（可留空清除）" clearable @change="applyBgImage" />
          <p style="margin:10px 0 4px">背景图填充:</p>
          <el-select v-model="bgImageSize" size="small" style="width:100%" @change="applyBgImage">
            <el-option value="cover" label="铺满(cover)" />
            <el-option value="contain" label="完整(contain)" />
            <el-option value="auto" label="原始大小(auto)" />
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
        <el-form-item label="背景图"><el-input v-model="pageBgImage" placeholder="文档背景图片 URL（可留空）" clearable /></el-form-item>
      </el-form>
      <template #footer><el-button @click="pageSettingsVisible=false">确定</el-button></template>
    </el-dialog>

    <!-- 添加数据集对话框 -->
    <el-dialog v-model="dsDialogVisible" title="添加数据集" width="740px">
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
          <el-form-item label="SQL">
            <div style="width:100%">
              <div style="display:flex;gap:6px;margin-bottom:6px">
                <el-button size="small" icon="Collection" @click="openDsDbBrowser('add')">表/视图/存储过程</el-button>
                <el-button size="small" icon="VideoPlay" type="primary" plain @click="openDsTest('add')">测试 SQL</el-button>
              </div>
              <el-input v-model="dsForm.customSql" type="textarea" :rows="3" placeholder="SELECT * FROM t WHERE id=@id" class="code-editor" />
            </div>
          </el-form-item>
          <el-form-item label="字段中文对照">
            <div style="width:100%">
              <div style="display:flex;gap:6px;margin-bottom:6px">
                <el-button size="small" icon="MagicStick" @click="generateDsMapping('add')" :loading="mappingLoading">自动生成</el-button>
                <span style="font-size:12px;color:#909399;line-height:24px">格式：字段=中文注释，一行一条</span>
              </div>
              <el-input v-model="dsForm.columnMapping" type="textarea" :rows="3"
                placeholder="自动生成：单表取字段中文注释（无注释则 字段=字段），其它 SQL 按实际查询列生成 字段=字段" class="code-editor" />
            </div>
          </el-form-item>
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
    <el-dialog v-model="dsEditVisible" title="编辑数据集" width="740px">
      <el-form :model="dsEditForm" label-width="90px" size="small">
        <el-form-item label="名称"><el-input v-model="dsEditForm.name" /></el-form-item>
        <el-form-item label="数据来源"><el-input :value="sourceLabel(dsEditForm.sourceType)" disabled /></el-form-item>
        <template v-if="dsEditForm.sourceType==='dataview'">
          <el-form-item label="选择视图"><el-select v-model="dsEditForm.sourceRef" style="width:100%"><el-option v-for="dv in dvList" :key="dv.id" :label="dv.name" :value="String(dv.id)" /></el-select></el-form-item>
        </template>
        <template v-if="dsEditForm.sourceType==='sql'">
          <el-form-item label="数据源"><el-select v-model="dsEditForm.dataSourceId" style="width:100%"><el-option v-for="ds_ in dsList" :key="ds_.id" :label="ds_.dataSourceName" :value="ds_.id" /></el-select></el-form-item>
          <el-form-item label="SQL">
            <div style="width:100%">
              <div style="display:flex;gap:6px;margin-bottom:6px">
                <el-button size="small" icon="Collection" @click="openDsDbBrowser('edit')">表/视图/存储过程</el-button>
                <el-button size="small" icon="VideoPlay" type="primary" plain @click="openDsTest('edit')">测试 SQL</el-button>
              </div>
              <el-input v-model="dsEditForm.customSql" type="textarea" :rows="4" class="code-editor" />
            </div>
          </el-form-item>
          <el-form-item label="字段中文对照">
            <div style="width:100%">
              <div style="display:flex;gap:6px;margin-bottom:6px">
                <el-button size="small" icon="MagicStick" @click="generateDsMapping('edit')" :loading="mappingLoading">自动生成</el-button>
                <span style="font-size:12px;color:#909399;line-height:24px">格式：字段=中文注释，一行一条</span>
              </div>
              <el-input v-model="dsEditForm.columnMapping" type="textarea" :rows="3"
                placeholder="自动生成：单表取字段中文注释（无注释则 字段=字段），其它 SQL 按实际查询列生成 字段=字段" class="code-editor" />
            </div>
          </el-form-item>
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

    <!-- 单元格公式语法帮助 -->
    <el-dialog v-model="formulaHelpVisible" title="❓ 单元格公式语法帮助" width="680px" append-to-body>
      <div class="formula-help">
        <div class="fh-section">一、占位符（数据行 / 表头 / 汇总行均可使用）</div>
        <pre>${字段名}         当前数据行该字段的值，如 ${name}
${数据集.字段}    指定数据集的字段（跨数据集引用，取第 0 行），如 ${订单.amount}
${rowIndex}       数据行行号（从 1 开始）</pre>
        <div class="fh-section">二、聚合占位符（汇总行绑定数据集后生效）</div>
        <pre>${金额:SUM}        合计
${金额:AVG}        平均值
${金额:MIN}        最小值
${金额:MAX}        最大值
${金额:COUNT}      条数
（支持 ${数据集.金额:SUM} 指定数据集）</pre>
        <div class="fh-section">三、公式（= 开头，数据行可用当前行字段参与计算）</div>
        <pre>=IF(${amount} &gt;= 100, '大额', '小额')   条件判断
=${amount} * 1.1                  算术
=ROUND(${amount} / 3, 2)          四舍五入
=SUM(A1:A10)                      静态单元格范围求和
=UPPER(${name})                   转大写</pre>
        <div class="fh-section">四、可用函数</div>
        <pre>SUM / AVG / MIN / MAX / COUNT    统计（支持范围引用 A1:A10）
IF(条件, 真值, 假值)              条件函数
SUBSTR(文本, 起, 长)              截取    LEN 长度
UPPER / LOWER / TRIM              大小写 / 去空格
CONCAT(a, b)                      拼接    ROUND(x, 位)  ABS(x)
NOW() / TODAY() / ROW()           当前时间 / 日期 / 行号</pre>
        <div class="fh-section">五、运算符</div>
        <pre>算术: + - * / %        比较: == != &gt; &lt; &gt;= &lt;=
逻辑: &amp;&amp; || !          三元: 条件 ? 真值 : 假值</pre>
        <div class="fh-note" style="margin-top:8px">
          占位符在数据行逐行替换；汇总行用聚合占位符。公式以 = 开头时用公式引擎计算，优先级：先替换占位符再算公式。
        </div>
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

    <!-- 自定义SQL数据集：数据库对象浏览 -->
    <DbObjectBrowser v-model:visible="dsDbBrowserVisible"
      :data-source-name="activeSqlDs()?.dataSourceName || ''"
      :data-source-type="activeSqlDs()?.dataSourceType || 'mysql'"
      @generated="onDsSqlGenerated" />

    <!-- 自定义SQL数据集：测试 SQL -->
    <SqlTestDialog v-model:visible="dsTestVisible"
      :data-source-id="activeSqlForm.dataSourceId" :sql="activeSqlForm.customSql" :params="[]" />
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Edit, View, Refresh, Loading, Close } from '@element-plus/icons-vue'
import request from '../../utils/request'
import DbObjectBrowser from '../../components/DbObjectBrowser.vue'
import SqlTestDialog from '../../components/SqlTestDialog.vue'
import { generateColumnMapping } from '../../utils/dbAssist'

const route = useRoute()
const router = useRouter()
const rptId = Number(route.params.id) || 0

const form = reactive({ id:0, name:'', groupName:'', sourceType:'dataview', sourceRef:'', customSql:'', paramsConfig:'[]', layoutJson:'{}', status:1 })
const dvList = ref<any[]>([]), dsList = ref<any[]>([]), flowList = ref<any[]>([]), apiList = ref<any[]>([])

// 数据集管理
interface Dataset { id: string; name: string; sourceType: string; sourceRef: string; customSql: string; dataSourceId: string|number; columnMapping?: string; fields: string[]; expanded: boolean; loading: boolean }
const datasets = ref<Dataset[]>([])
const dsDialogVisible = ref(false), dsEditVisible = ref(false), dsEditIdx = ref(-1)
const dsForm = reactive({ name:'', sourceType:'dataview', sourceRef:'', customSql:'', dataSourceId:'', columnMapping:'' })
const dsEditForm = reactive({ name:'', sourceType:'', sourceRef:'', customSql:'', dataSourceId:'', columnMapping:'' })
// 自定义SQL数据集辅助：数据库对象浏览 / 测试 SQL
const dsDbBrowserVisible = ref(false)
const dsTestVisible = ref(false)
const dsSqlMode = ref<'add' | 'edit'>('add')
const mappingLoading = ref(false)
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
// 边框设置（分边/粗细/颜色/线型/交叉斜线）
const bdState = reactive({ top: true, bottom: true, left: true, right: true, diagonal: false })
const bdStyle = ref('solid'), bdWidth = ref(1), bdColor = ref('#cccccc')
// 单元格背景图 / 文档背景图
const bgImage = ref(''), bgImageSize = ref('cover')
const pageBgImage = ref('')
const cellValue = ref(''), rowType = ref('data')
// 行类型与数据集绑定（完整行语义模型）
const rowTypes = ref<string[]>([])            // 每行类型: title/header/data/footer
const rowDatasets = ref<Record<number, string>>({})  // 行号 → 数据集id
const rowDataset = ref('')                    // 当前选中行绑定的数据集
const rowZebras = ref<Record<number, boolean>>({})   // 行号 → 斑马纹开关
const rowZebra = ref(false)                   // 当前选中行的斑马纹
const showPreview = ref(false), pageSettingsVisible = ref(false)
const formulaHelpVisible = ref(false)
const previewHtml = ref(''), previewParams = ref<any[]>([])
const previewValues = ref<Record<string,any>>({})

const canMerge = computed(() => selectedCells.value.length >= 2)
const hasSelection = computed(() => selR.value >= 0 && selC.value >= 0)

// 撤销/重做
const MAX_HISTORY = 50
const undoStack = ref<string[]>([])
const redoStack = ref<string[]>([])
const canUndo = computed(() => undoStack.value.length > 0)
const canRedo = computed(() => redoStack.value.length > 0)

function snapState() {
  return JSON.stringify({ cells: cells.value, rows: rowHeights.value, types: rowTypes.value.slice(), datasets: { ...rowDatasets.value }, zebras: { ...rowZebras.value }, cols: colWidths.value, maxR: maxRows.value, maxC: maxCols.value })
}

function pushHistory() {
  undoStack.value.push(snapState())
  if (undoStack.value.length > MAX_HISTORY) undoStack.value.shift()
  redoStack.value = []
}

function undo() {
  if (!canUndo.value) return
  redoStack.value.push(snapState())
  restoreState(undoStack.value.pop()!)
}

function redo() {
  if (!canRedo.value) return
  undoStack.value.push(snapState())
  restoreState(redoStack.value.pop()!)
}

function restoreState(snap: string) {
  try {
    const s = JSON.parse(snap)
    cells.value = s.cells || {}
    rowHeights.value = s.rows || []
    rowTypes.value = s.types || []
    rowDatasets.value = s.datasets || {}
    rowZebras.value = s.zebras || {}
    colWidths.value = s.cols || []
    maxRows.value = s.maxR || 10
    maxCols.value = s.maxC || 6
  } catch {}
}

const pageSizes: Record<string, [number, number]> = { A4: [794, 1123], A3: [1123, 1587], Letter: [816, 1056], Legal: [816, 1344] }
const pageWidth = computed(() => pageOrientation.value === 'landscape' ? (pageSizes[pageSize.value]||pageSizes.A4)[1] : (pageSizes[pageSize.value]||pageSizes.A4)[0])
const pageHeight = computed(() => pageOrientation.value === 'landscape' ? (pageSizes[pageSize.value]||pageSizes.A4)[0] : (pageSizes[pageSize.value]||pageSizes.A4)[1])
const showPageBreak = computed(() => {
  let totalH = 0
  for (let r=0; r<maxRows.value; r++) totalH += typeof rowHeights.value[r]==='string' ? 25 : (Number(rowHeights.value[r])||25)
  return totalH > pageHeight.value
})

onMounted(() => {
  document.addEventListener('keydown', (e) => {
    if (e.key==='Control') ctrlDown.value = true
    if (e.ctrlKey && e.key==='z') { e.preventDefault(); undo() }
    if (e.ctrlKey && e.key==='y') { e.preventDefault(); redo() }
  })
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
  Object.assign(dsEditForm, { name: ds.name, sourceType: ds.sourceType, sourceRef: ds.sourceRef, customSql: ds.customSql, dataSourceId: ds.dataSourceId, columnMapping: ds.columnMapping || '' })
  dsEditVisible.value = true
}

function saveDsEdit() {
  if (dsEditIdx.value < 0) return
  const ds = datasets.value[dsEditIdx.value]
  ds.name = dsEditForm.name; ds.sourceRef = dsEditForm.sourceRef; ds.customSql = dsEditForm.customSql
  ds.dataSourceId = dsEditForm.dataSourceId; ds.columnMapping = dsEditForm.columnMapping
  dsEditVisible.value = false
  loadDsFields(dsEditIdx.value)
}

// ===== 自定义SQL数据集辅助（表/视图/存储过程 + 测试 + 字段中文对照） =====

/** 当前操作的 SQL 表单（添加 or 编辑） */
const activeSqlForm = computed(() => dsSqlMode.value === 'add' ? dsForm : dsEditForm)

function activeSqlDs(): any {
  const id = activeSqlForm.value.dataSourceId
  return dsList.value.find((d: any) => d.id === id)
}

function openDsDbBrowser(mode: 'add' | 'edit') {
  if (!activeSqlDs()) { ElMessage.warning('请先选择数据源'); return }
  dsSqlMode.value = mode
  dsDbBrowserVisible.value = true
}
function openDsTest(mode: 'add' | 'edit') {
  if (!activeSqlForm.value.dataSourceId) { ElMessage.warning('请先选择数据源'); return }
  if (!activeSqlForm.value.customSql?.trim()) { ElMessage.warning('请先编写 SQL'); return }
  dsSqlMode.value = mode
  dsTestVisible.value = true
}
function onDsSqlGenerated(sql: string) {
  activeSqlForm.value.customSql = sql
}

async function generateDsMapping(mode: 'add' | 'edit') {
  const f = mode === 'add' ? dsForm : dsEditForm
  const ds = activeSqlDs()
  if (!ds) { ElMessage.warning('请先选择数据源'); return }
  if (!f.customSql?.trim()) { ElMessage.warning('请先编写 SQL'); return }
  mappingLoading.value = true
  try {
    const lines = await generateColumnMapping({
      dataSourceId: ds.id, dataSourceName: ds.dataSourceName, sql: f.customSql
    })
    f.columnMapping = lines.join('\n')
    ElMessage.success(`已生成 ${lines.length} 条字段对照`)
  } catch { ElMessage.error('生成失败，请检查 SQL 与数据源') } finally { mappingLoading.value = false }
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
  Object.assign(dsForm, { name:'', sourceType:'dataview', sourceRef:'', customSql:'', dataSourceId:'', columnMapping:'' })
  dsDialogVisible.value = true
}

function addDataset() {
  if (!dsForm.name) { dsForm.name = dsForm.sourceType + '_' + (datasets.value.length+1) }
  const ds: Dataset = { id: Date.now().toString(), name: dsForm.name, sourceType: dsForm.sourceType, sourceRef: dsForm.sourceRef, customSql: dsForm.customSql, dataSourceId: dsForm.dataSourceId, columnMapping: dsForm.columnMapping, fields: [], expanded: true, loading: false }
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
    rowTypes.value = layout.rows?.map((r:any)=> r.type || 'header') || []
    rowDatasets.value = {}
    layout.rows?.forEach((r:any, i:number) => { if (r.dataset) rowDatasets.value[i] = r.dataset })
    rowZebras.value = {}
    layout.rows?.forEach((r:any, i:number) => { if (r.zebra === true) rowZebras.value[i] = true })
    colWidths.value = layout.cols?.map((c:any)=>c.width||100) || []
    pageSize.value = layout.page?.size||'A4'; pageOrientation.value = layout.page?.orientation||'portrait'
    if (layout.page?.margin) Object.assign(pageMargin, layout.page.margin)
    pageHeader.value = layout.page?.header||''; pageFooter.value = layout.page?.footer||''
    pageBgImage.value = layout.page?.bgImage||''
    cells.value = {}; if (layout.cells) for (const c of layout.cells) cells.value[`${c.r},${c.c}`] = c
    previewParams.value = layout.params||[]
    if (layout.datasets) datasets.value = layout.datasets.map((d:any)=>({...d,expanded:true,loading:false}))
  } catch {}
}

function resizeGrid() { while(rowHeights.value.length<maxRows.value) { rowHeights.value.push(25); rowTypes.value.push('header') } while(colWidths.value.length<maxCols.value) colWidths.value.push(100) }
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
  // 边框：false=无；对象=分边/粗细/颜色/线型/交叉斜线；缺省=默认细边框
  const b = s.border
  if (b === false) style+='border:none;'
  else if (b && typeof b === 'object') {
    const w=b.width||1, col=b.color||'#333', st=b.style||'solid'
    style+='border:none;'
    if (b.top) style+=`border-top:${w}px ${st} ${col};`
    if (b.bottom) style+=`border-bottom:${w}px ${st} ${col};`
    if (b.left) style+=`border-left:${w}px ${st} ${col};`
    if (b.right) style+=`border-right:${w}px ${st} ${col};`
    if (b.diagonal) style+=`background-image:linear-gradient(to top right,transparent calc(50% - ${w*0.5}px),${col},transparent calc(50% + ${w*0.5}px)),linear-gradient(to bottom right,transparent calc(50% - ${w*0.5}px),${col},transparent calc(50% + ${w*0.5}px));`
  }
  // 单元格背景图
  if (s.bgImage) style+=`background-image:url('${s.bgImage}');background-size:${s.bgImageSize||'cover'};background-position:center;background-repeat:no-repeat;`
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
  rowType.value = rowTypes.value[r] || (rowHeights.value[r]==='data'?'data':'header')
  rowDataset.value = rowDatasets.value[r] || ''
  rowZebra.value = !!rowZebras.value[r]
  boldActive.value = cell?.style?.bold||false
  italicActive.value = cell?.style?.italic||false
  selFontSize.value = cell?.style?.fontSize||12
  selColor.value = cell?.style?.color||''; selBgColor.value = cell?.style?.bgColor||''
  loadBorderState(cell)
}

function commitEdit() {
  if (selR.value<0||selC.value<0) return
  const key=`${selR.value},${selC.value}`, existing=cells.value[key]||{}
  if (existing.value !== cellValue.value) {
    pushHistory()
    cells.value[key]={...existing, value: cellValue.value}
  }
}

function onCellInput(_e:Event, _r:number, _c:number) {
  // 不立即更新cells数据，避免Vue重渲染导致光标跳动
  // 失焦时通过onCellBlur自动保存
}
function onCellBlur(_r:number, _c:number) {
  // 从DOM读取实际文本，保存到cells
  if (selR.value<0||selC.value<0) return
  const td = document.querySelector(`td[data-r="${selR.value}"][data-c="${selC.value}"]`)
  if (!td) return
  const text = td.textContent || ''
  const key=`${selR.value},${selC.value}`, existing=cells.value[key]||{}
  if (existing.value !== text) {
    pushHistory()
    cells.value[key]={...existing, value: text}
    cellValue.value = text
  }
}
function onCellDelete(r:number, c:number) {
  pushHistory()
  const key=`${r},${c}`, existing=cells.value[key]||{}
  cells.value[key]={...existing, value: ''}
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

function updateRowType() {
  if (selR.value < 0) return
  rowTypes.value[selR.value] = rowType.value
  // 标题/表头/汇总行给视觉行高提示，数据行自适应
  rowHeights.value[selR.value] = rowType.value==='title'?35 : rowType.value==='header'?28 : rowType.value==='footer'?25 : 'data'
}
function updateRowDataset() {
  if (selR.value < 0) return
  if (rowDataset.value) rowDatasets.value[selR.value] = rowDataset.value
  else delete rowDatasets.value[selR.value]
}
function updateRowZebra() {
  if (selR.value < 0) return
  if (rowZebra.value) rowZebras.value[selR.value] = true
  else delete rowZebras.value[selR.value]
}

function applyStyle(prop:string, val?:any) {
  pushHistory()
  forEachSelected((r,c) => {
    const key=`${r},${c}`, existing=cells.value[key]||{value:''}
    const style = existing.style||{}
    if (prop==='bold') { style.bold = val!==undefined?val:!style.bold; boldActive.value=style.bold }
    else if (prop==='italic') { style.italic = val!==undefined?val:!style.italic; italicActive.value=style.italic }
    else if (prop==='underline') { style.underline = val!==undefined?val:!style.underline }
    else if (val===null || val===undefined) { delete (style as any)[prop] }   // 清除颜色等
    else { (style as any)[prop] = val }
    cells.value[key] = {...existing, style}
  })
}

/** 字体/字号/颜色选择器变更（v-model 已更新，读取最新值写入选中单元格） */
function onStyleChange(prop: 'color' | 'bgColor' | 'fontName' | 'fontSize') {
  const val = prop === 'color' ? selColor.value
    : prop === 'bgColor' ? selBgColor.value
    : prop === 'fontName' ? selFontName.value
    : selFontSize.value
  applyStyle(prop, val)
}

// ===== 边框设置（分边/粗细/颜色/线型/交叉斜线） =====
function loadBorderState(cell: any) {
  const b = cell?.style?.border
  if (b && typeof b === 'object') {
    bdState.top = !!b.top; bdState.bottom = !!b.bottom; bdState.left = !!b.left; bdState.right = !!b.right; bdState.diagonal = !!b.diagonal
    bdStyle.value = b.style || 'solid'; bdWidth.value = b.width || 1; bdColor.value = b.color || '#cccccc'
  } else {
    bdState.top = bdState.bottom = bdState.left = bdState.right = true; bdState.diagonal = false
    bdStyle.value = 'solid'; bdWidth.value = 1; bdColor.value = '#cccccc'
  }
  bgImage.value = cell?.style?.bgImage || ''
  bgImageSize.value = cell?.style?.bgImageSize || 'cover'
}

function applyBorder() {
  pushHistory()
  forEachSelected((r:number, c:number) => {
    const key=`${r},${c}`, existing=cells.value[key]||{value:''}
    const style = {...(existing.style||{})}
    style.border = { top:bdState.top, bottom:bdState.bottom, left:bdState.left, right:bdState.right, diagonal:bdState.diagonal, width:bdWidth.value, style:bdStyle.value, color:bdColor.value }
    cells.value[key] = {...existing, style}
  })
}
function setBorderSide(side: 'top'|'bottom'|'left'|'right') { bdState[side] = !bdState[side]; applyBorder() }
function setBorderAll(on: boolean) { bdState.top = bdState.bottom = bdState.left = bdState.right = on; applyBorder() }
function setBorderDiagonal() { bdState.diagonal = !bdState.diagonal; applyBorder() }
function applyBorderMeta() { applyBorder() }

/** 单元格背景图 */
function applyBgImage() {
  pushHistory()
  forEachSelected((r:number, c:number) => {
    const key=`${r},${c}`, existing=cells.value[key]||{value:''}
    const style = {...(existing.style||{})}
    if (bgImage.value) { style.bgImage = bgImage.value; style.bgImageSize = bgImageSize.value }
    else { delete style.bgImage; delete style.bgImageSize }
    cells.value[key] = {...existing, style}
  })
}

function toggleBorder() {
  pushHistory()
  forEachSelected((r:number,c:number) => {
    const key=`${r},${c}`, ex=cells.value[key]||{value:''}, s={...(ex.style||{})}
    const b = s.border
    const hasAny = b === undefined || b === true || (typeof b==='object' && (b.top||b.bottom||b.left||b.right))
    s.border = hasAny ? false : { top:true,bottom:true,left:true,right:true,width:1,style:'solid',color:'#cccccc' }
    cells.value[key] = {...ex, style:s}
  })
  loadBorderState(getCell(selR.value, selC.value))
}

function mergeSelected() {
  if (selectedCells.value.length<2) return
  pushHistory()
  const rs = selectedCells.value.map(([r])=>r), cs = selectedCells.value.map(([,c])=>c)
  const r1=Math.min(...rs), r2=Math.max(...rs), c1=Math.min(...cs), c2=Math.max(...cs)
  const base = cells.value[`${r1},${c1}`]||{value:''}
  cells.value[`${r1},${c1}`] = {...base, colspan:c2-c1+1, rowspan:r2-r1+1}
}

function splitSelected() {
  if (selR.value<0||selC.value<0) return
  pushHistory()
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

function insertRow(dir: number) {
  pushHistory()
  if (selR.value<0) selR.value = maxRows.value - 1
  const r = dir<0 ? selR.value : selR.value + 1
  const newCells: Record<string,any> = {}
  for (const [key, cell] of Object.entries(cells.value)) {
    const [cr, cc] = key.split(',').map(Number)
    if (cr >= r) newCells[`${cr+1},${cc}`] = cell
    else newCells[key] = cell
  }
  cells.value = newCells
  rowHeights.value.splice(r, 0, 25)
  rowTypes.value.splice(r, 0, 'header')
  // 数据集绑定/斑马纹按新行号重映射
  const newDs: Record<number, string> = {}
  for (const [k, v] of Object.entries(rowDatasets.value)) {
    const idx = Number(k)
    newDs[idx >= r ? idx + 1 : idx] = v
  }
  rowDatasets.value = newDs
  const newZb: Record<number, boolean> = {}
  for (const [k, v] of Object.entries(rowZebras.value)) {
    const idx = Number(k)
    newZb[idx >= r ? idx + 1 : idx] = v
  }
  rowZebras.value = newZb
  maxRows.value++
  if (dir > 0) selR.value++
}

function deleteRow() {
  if (selR.value<0 || maxRows.value<=1) return
  pushHistory()
  const r = selR.value
  const newCells: Record<string,any> = {}
  for (const [key, cell] of Object.entries(cells.value)) {
    const [cr, cc] = key.split(',').map(Number)
    if (cr > r) newCells[`${cr-1},${cc}`] = cell
    else if (cr < r) newCells[key] = cell
    // cr === r: drop
  }
  cells.value = newCells
  rowHeights.value.splice(r, 1)
  rowTypes.value.splice(r, 1)
  // 数据集绑定/斑马纹按新行号重映射
  const newDs: Record<number, string> = {}
  for (const [k, v] of Object.entries(rowDatasets.value)) {
    const idx = Number(k)
    if (idx > r) newDs[idx - 1] = v
    else if (idx < r) newDs[idx] = v
  }
  rowDatasets.value = newDs
  const newZb: Record<number, boolean> = {}
  for (const [k, v] of Object.entries(rowZebras.value)) {
    const idx = Number(k)
    if (idx > r) newZb[idx - 1] = v
    else if (idx < r) newZb[idx] = v
  }
  rowZebras.value = newZb
  maxRows.value--
  selR.value = Math.min(selR.value, maxRows.value - 1)
}

function insertCol(dir: number) {
  pushHistory()
  if (selC.value<0) selC.value = maxCols.value - 1
  const c = dir<0 ? selC.value : selC.value + 1
  const newCells: Record<string,any> = {}
  for (const [key, cell] of Object.entries(cells.value)) {
    const [cr, cc] = key.split(',').map(Number)
    if (cc >= c) newCells[`${cr},${cc+1}`] = cell
    else newCells[key] = cell
  }
  cells.value = newCells
  colWidths.value.splice(c, 0, 100)
  maxCols.value++
  if (dir > 0) selC.value++
}

function deleteCol() {
  if (selC.value<0 || maxCols.value<=1) return
  pushHistory()
  const c = selC.value
  const newCells: Record<string,any> = {}
  for (const [key, cell] of Object.entries(cells.value)) {
    const [cr, cc] = key.split(',').map(Number)
    if (cc > c) newCells[`${cr},${cc-1}`] = cell
    else if (cc < c) newCells[key] = cell
  }
  cells.value = newCells
  colWidths.value.splice(c, 1)
  maxCols.value--
  selC.value = Math.min(selC.value, maxCols.value - 1)
}

function onCellDrop(e:DragEvent, r:number, c:number) { const field = e.dataTransfer?.getData('field'); if (field) { const key=`${r},${c}`,ex=cells.value[key]||{}; cells.value[key]={...ex,value:field} } }

function onGridScroll() {}

function saveLayoutJson() {
  const rows=[]; for (let r=0;r<maxRows.value;r++) rows.push({
    height:typeof rowHeights.value[r]==='string'?25:(rowHeights.value[r]||25),
    type:rowTypes.value[r] || (rowHeights.value[r]==='data'?'data':'header'),
    dataset:rowDatasets.value[r] || '',
    expand:(rowTypes.value[r]==='data' && rowDatasets.value[r]) ? 'auto' : '',
    zebra:rowZebras.value[r] === true
  })
  const cols=[]; for (let c=0;c<maxCols.value;c++) cols.push({width:colWidths.value[c]||100})
  const cellArr=Object.entries(cells.value).map(([k,v]:any)=>({r:Number(k.split(',')[0]),c:Number(k.split(',')[1]),...v}))
  const dsArr = datasets.value.map(d=>({id:d.id,name:d.name,sourceType:d.sourceType,sourceRef:d.sourceRef,customSql:d.customSql,dataSourceId:d.dataSourceId,fields:d.fields}))
  form.layoutJson = JSON.stringify({
    page:{size:pageSize.value,orientation:pageOrientation.value,margin:{...pageMargin},header:pageHeader.value,footer:pageFooter.value,bgImage:pageBgImage.value},
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
.code-editor :deep(textarea) { font-family: Consolas, Monaco, 'Courier New', monospace; font-size: 12px; }
.formula-help { max-height: 60vh; overflow-y: auto; }
.formula-help pre { background: #f6f8fa; border: 1px solid #e4e7ed; border-radius: 6px; padding: 8px 10px; font-size: 12px; line-height: 1.9; margin: 0 0 4px; overflow-x: auto; font-family: Consolas, Monaco, 'Courier New', monospace; white-space: pre; }
.formula-help .fh-section { font-weight: 600; margin: 12px 0 4px; color: #303133; }
.formula-help .fh-section:first-child { margin-top: 0; }
.formula-help .fh-note { color: #909399; font-size: 12px; }
.toolbar{display:flex;justify-content:space-between;align-items:center;padding:6px 12px;background:#001529;color:#fff;flex-shrink:0}
.designer-body{flex:1;display:flex;overflow:hidden}
.left-panel{width:220px;border-right:1px solid #ddd;padding:6px;overflow-y:auto;background:#fff;flex-shrink:0}
.center-panel{flex:1;display:flex;flex-direction:column;overflow:hidden}
.style-toolbar{display:flex;gap:4px;padding:4px 8px;background:#fff;border-bottom:1px solid #ddd;flex-wrap:wrap;align-items:center;flex-shrink:0}
.right-panel{width:210px;border-left:1px solid #ddd;padding:8px;overflow-y:auto;background:#fff;flex-shrink:0}
.field-item{padding:4px 8px;margin-bottom:2px;background:#e6f7ff;border-radius:4px;cursor:grab;font-size:12px}
.field-item:hover{background:#bae7ff}
.grid-wrapper{flex:1;overflow:auto;padding:16px;background:#818181;display:flex;justify-content:center}
.rpt-page{background:#fff;box-shadow:0 2px 12px rgba(0,0,0,0.3);position:relative;padding-bottom:0}
.page-break{position:absolute;left:0;right:0;border-top:2px dashed #ff4d4f;text-align:center;pointer-events:none}
.page-break-label{font-size:10px;color:#ff4d4f;background:#818181;padding:0 8px;position:relative;top:-8px}
.rpt-grid{border-collapse:collapse;background:#fff;font-size:12px;table-layout:fixed;width:100%}
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
