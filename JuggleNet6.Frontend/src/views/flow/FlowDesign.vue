<template>
  <div class="designer-container" @keydown.delete="onDeleteKey" @keydown.ctrl.z.prevent="undo" @keydown.ctrl.y.prevent="redo" tabindex="0" ref="containerRef">
    <!-- 顶部工具栏 -->
    <div class="toolbar">
      <div class="toolbar-left">
        <el-button icon="ArrowLeft" link @click="router.back()" style="color:#fff">返回</el-button>
        <span class="flow-title">{{ flowInfo?.flowName }} - 流程设计器</span>
      </div>
      <div class="toolbar-center">
      </div>
      <div class="toolbar-right">
        <el-tooltip content="撤销 (Ctrl+Z)"><el-button size="small" icon="RefreshLeft" :disabled="undoStack.length === 0" @click="undo" /></el-tooltip>
        <el-tooltip content="重做 (Ctrl+Y)"><el-button size="small" icon="RefreshRight" :disabled="redoStack.length === 0" @click="redo" /></el-tooltip>
        <el-button size="small" @click="autoLayout" icon="Grid">自动布局</el-button>
        <el-button size="small" type="warning" @click="openAiDialog" icon="MagicStick">AI 生成</el-button>
        <el-button size="small" @click="paramDrawer = true" icon="Setting">流程参数</el-button>
        <el-button size="small" @click="variableDrawer = true" icon="List">变量</el-button>
        <el-button size="small" type="warning" @click="openDebug">调试</el-button>
        <el-button size="small" type="success" @click="saveFlow">保存</el-button>
        <el-button size="small" type="primary" @click="deployFlow">部署</el-button>
      </div>
    </div>

    <div class="designer-body">
      <!-- 左侧节点工具面板 -->
      <div class="node-panel">
        <div class="node-panel-title">节点工具</div>
        <div class="node-panel-body">
          <div
            v-for="node in nodeToolList"
            :key="node.type"
            class="node-tool-item"
            :class="'nt-' + node.type.toLowerCase()"
            :style="{ opacity: (node.type === 'START' && hasStart) || (node.type === 'END' && hasEnd) ? 0.4 : 1, pointerEvents: (node.type === 'START' && hasStart) || (node.type === 'END' && hasEnd) ? 'none' : 'auto' }"
            @click="addNode(node.type)"
          >
            <span class="nt-icon">{{ node.icon }}</span>
            <span class="nt-label">{{ node.label }}</span>
          </div>
        </div>
      </div>

      <!-- 中间 VueFlow 画布 -->
      <div class="canvas-area" ref="canvasRef">
        <VueFlow
          v-model:nodes="vfNodes"
          v-model:edges="vfEdges"
          :default-viewport="{ x: 60, y: 40, zoom: 1 }"
          :min-zoom="0.2"
          :max-zoom="2"
          :snap-to-grid="true"
          :snap-grid="[16, 16]"
          fit-view-on-init
          @node-click="onVfNodeClick"
          @edge-click="onVfEdgeClick"
          @connect="onVfConnect"
          @edge-update="onVfEdgeUpdate"
          @pane-click="onPaneClick"
          class="vf-canvas"
        >
          <Background :variant="'dots'" :gap="20" :size="1.2" :color="'#d0d7e3'" />
          <Controls />
          <MiniMap :node-color="vfNodeColor" :node-border-radius="8" />

          <!-- 自定义节点模板 -->
          <template #node-juggle="{ data }">
            <div
              class="jg-node"
              :class="[
                'jg-' + data.elementType.toLowerCase(),
                selectedNodeKey === data.nodeKey ? 'jg-selected' : '',
                debugNodeStatus[data.nodeKey] === 'success' ? 'jg-debug-success' : '',
                debugNodeStatus[data.nodeKey] === 'fail' ? 'jg-debug-fail' : '',
                debugNodeStatus[data.nodeKey] === 'running' ? 'jg-debug-running' : ''
              ]"
              @click.stop="selectNodeByKey(data.nodeKey)"
            >
              <Handle type="target" :position="Position.Top" class="jg-handle jg-handle-top" />
              <!-- 调试状态图标 -->
              <div v-if="debugNodeStatus[data.nodeKey]" class="jg-debug-badge">
                <span v-if="debugNodeStatus[data.nodeKey] === 'success'">✓</span>
                <span v-else-if="debugNodeStatus[data.nodeKey] === 'fail'">✗</span>
                <span v-else>⏳</span>
              </div>
              <div class="jg-icon">{{ nodeIcon(data.elementType) }}</div>
              <div class="jg-name">{{ data.label || data.nodeKey }}</div>
              <div class="jg-type">{{ nodeTypeName(data.elementType) }}</div>
              <!-- 节点报错提示 -->
              <div
                v-if="debugNodeStatus[data.nodeKey] === 'fail' && debugNodeError[data.nodeKey]"
                class="jg-debug-error-tip"
                :title="debugNodeError[data.nodeKey]"
                @click.stop="showNodeDebugDetail(data.nodeKey)"
              >
                ⚠️ {{ debugNodeError[data.nodeKey] }}
              </div>
              <!-- 调试输出变量预览 -->
              <div v-if="debugNodeOutput[data.nodeKey] && debugNodeStatus[data.nodeKey] !== 'fail'" class="jg-debug-output" @click.stop="showNodeDebugDetail(data.nodeKey)">
                📊 查看输出
              </div>
              <Handle type="source" :position="Position.Bottom" class="jg-handle jg-handle-bottom" />
            </div>
          </template>
        </VueFlow>

        <!-- 空状态提示 -->
        <div class="flow-hint" v-if="vfNodes.length === 0">
          <div style="font-size:48px;color:#ddd">⬡</div>
          <p>从左侧节点面板点击添加节点，然后拖拽连接线建立流程</p>
        </div>

        <!-- 删除提示（选中时显示） -->
        <div class="delete-hint" v-if="selectedNodeKey || selectedEdgeId">
          <span v-if="selectedNodeKey">已选中节点：<b>{{ selectedNodeKey }}</b></span>
          <span v-if="selectedEdgeId">已选中连线</span>
          &nbsp;&nbsp;按 <kbd>Delete</kbd> 删除
        </div>
      </div>

      <!-- 右侧属性面板 -->
      <div class="right-panel" @keydown.stop @keyup.stop>
        <div class="panel-title" v-if="selectedEdgeId && !selectedNodeKey">
          <span style="color:#1890ff">━</span>
          连线属性
          <el-button size="small" type="danger" link icon="Delete"
            style="margin-left:auto" @click="removeEdge(selectedEdgeId)">删除连线</el-button>
        </div>
        <div class="panel-title" v-else-if="selectedNode">
          <span :class="'type-dot-' + selectedNode.elementType.toLowerCase()">●</span>
          {{ nodeTypeName(selectedNode.elementType) }} 属性
          <el-button size="small" type="danger" link icon="Delete"
            style="margin-left:auto" @click="removeNode(selectedNode.key)">删除</el-button>
        </div>
        <div class="panel-title" v-else>节点属性</div>
        <!-- 连线选中时显示删除面板 -->
        <div v-if="selectedEdgeId && !selectedNodeKey" class="prop-content">
          <div class="prop-tip" style="background:#fff2f0;color:#ff4d4f">
            已选中该连线，可点击右上角「删除连线」或按 <b>Delete</b> 键删除。
          </div>
          <div v-if="selectedEdgeInfo" class="prop-item">
            <label>起始节点</label>
            <el-input :value="selectedEdgeInfo.source" disabled size="small" />
          </div>
          <div v-if="selectedEdgeInfo" class="prop-item">
            <label>目标节点</label>
            <el-input :value="selectedEdgeInfo.target" disabled size="small" />
          </div>
        </div>

        <div class="prop-content" v-if="selectedNode">
          <div class="prop-item">
            <label>节点Key</label>
            <el-input :value="selectedNode.key" disabled size="small" />
          </div>
          <div class="prop-item">
            <label>节点标签</label>
            <el-input v-model="selectedNode.label" placeholder="可选显示名称" size="small"
              @input="syncVfNodeLabel(selectedNode)" />
          </div>

          <!-- START 节点 -->
          <template v-if="selectedNode.elementType === 'START'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('START')">帮助</el-button></div>
            <div class="prop-tip">开始节点是流程入口。可在「流程参数」中设置入参。</div>
          </template>

          <!-- END 节点 -->
          <template v-if="selectedNode.elementType === 'END'">
            <div class="prop-tip">结束节点是流程出口。可在「流程参数」中设置出参。</div>
          </template>

          <!-- MERGE 节点 -->
          <template v-if="selectedNode.elementType === 'MERGE'">
            <div class="prop-tip">聚合节点：将多个 CONDITION 分支汇聚到一个执行路径。通过画布连线设置入口和出口。</div>
          </template>

          <!-- LOOP 循环节点属性 -->
          <template v-if="selectedNode.elementType === 'LOOP'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('LOOP')">帮助</el-button></div>
            <div class="prop-item">
              <label>数组变量名</label>
              <el-input v-model="selectedNode.loopConfig.arrayVariable" size="small" placeholder="要遍历的数组变量，如 items">
                <template #prepend>$var.getVariableValue('</template>
                <template #append>')</template>
              </el-input>
            </div>
            <div class="prop-item">
              <label>当前元素变量</label>
              <el-input v-model="selectedNode.loopConfig.itemVariable" size="small" placeholder="默认: _loop_item" />
            </div>
            <div class="prop-item">
              <label>当前索引变量</label>
              <el-input v-model="selectedNode.loopConfig.indexVariable" size="small" placeholder="默认: _loop_index" />
            </div>
            <div class="prop-item">
              <label>数组总数变量</label>
              <el-input v-model="selectedNode.loopConfig.totalVariable" size="small" placeholder="默认: _loop_total" />
            </div>
            <div class="prop-item">
              <label>结果收集变量</label>
              <el-input v-model="selectedNode.loopConfig.outputVariable" size="small" placeholder="默认: _loop_results" />
            </div>
            <div class="prop-tip" style="margin-top:8px">循环体内每次迭代会将当前元素、索引、总数写入指定变量。循环体中的后续节点可以使用这些变量。</div>
          </template>

          <!-- DELAY/WAIT 延迟节点属性 -->
          <template v-if="selectedNode.elementType === 'DELAY'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('DELAY')">帮助</el-button></div>
            <div class="prop-item">
              <label>延迟模式</label>
              <el-switch v-model="selectedNode.delayConfig.variableMode" active-text="变量动态" inactive-text="固定时间" size="small" />
            </div>
            <div class="prop-item" v-if="!selectedNode.delayConfig.variableMode">
              <label>延迟时间(ms)</label>
              <el-input-number v-model="selectedNode.delayConfig.delayMs" :min="0" :step="1000" size="small" style="width:100%" placeholder="毫秒" />
            </div>
            <div class="prop-item" v-else>
              <label>延迟变量</label>
              <el-input v-model="selectedNode.delayConfig.delayVariable" size="small" placeholder="变量名，值为毫秒数" />
            </div>
            <div class="prop-tip" style="margin-top:8px">延迟节点会暂停流程执行指定毫秒。适用于限流、等待、定时触发等场景。</div>
          </template>

          <!-- PARALLEL 并行节点属性 -->
          <template v-if="selectedNode.elementType === 'PARALLEL'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('PARALLEL')">帮助</el-button></div>
            <div class="prop-item">
              <label>等待模式</label>
              <el-radio-group v-model="selectedNode.parallelConfig.waitMode" size="small">
                <el-radio value="ALL_WAIT">全部等待</el-radio>
                <el-radio value="ANY_FAST">任一完成</el-radio>
              </el-radio-group>
            </div>
            <div class="prop-item">
              <label>并行超时(ms)</label>
              <el-input-number v-model="selectedNode.parallelConfig.timeout" :min="0" :step="1000" size="small" style="width:100%" placeholder="0=不限" />
            </div>
            <div class="prop-tip" style="margin-top:8px">并行节点会同时执行所有分支路径。通过画布连线设置各分支入口。<br/>全部等待：所有分支完成后继续。任一完成：任一分支完成即继续。</div>
          </template>

          <!-- TRANSFORM 模板转换节点属性 -->
          <template v-if="selectedNode.elementType === 'TRANSFORM'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('TRANSFORM')">帮助</el-button></div>
            <div class="prop-tip">模板转换：将模板占位符替换为变量/参数值，结果赋值到目标。</div>
            <div class="prop-item">
              <label>赋值给类型</label>
              <el-select v-model="selectedNode.transformConfig.targetType" size="small" style="width:100%">
                <el-option value="INPUT" label="入参" />
                <el-option value="OUTPUT" label="出参" />
                <el-option value="VARIABLE" label="中间变量" />
                <el-option value="STATIC" label="静态变量" />
              </el-select>
            </div>
            <div class="prop-item">
              <label>选择目标</label>
              <el-select v-model="selectedNode.transformConfig.targetCode" placeholder="选择" size="small" style="width:100%" filterable>
                <template v-if="selectedNode.transformConfig.targetType === 'INPUT'">
                  <el-option v-for="p in flowInputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                </template>
                <template v-else-if="selectedNode.transformConfig.targetType === 'OUTPUT'">
                  <el-option v-for="p in flowOutputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                </template>
                <template v-else-if="selectedNode.transformConfig.targetType === 'VARIABLE'">
                  <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
                </template>
                <template v-else-if="selectedNode.transformConfig.targetType === 'STATIC'">
                  <el-option v-for="s in staticVariables" :key="s.varCode" :value="s.varCode" :label="`${s.varName} (${s.varCode})`" />
                </template>
              </el-select>
            </div>
            <div class="prop-item">
              <label>格式化模板</label>
              <el-input v-model="selectedNode.transformConfig.template" type="textarea" :rows="8"
                placeholder='如: {"user":"${userName}","id":"${userId | ToFix(0)}","name":"${input_name | ToUpper}"}'
                class="code-editor" />
              <div style="font-size:11px;color:#999;margin-top:4px">
                语法: ${变量名|管道1|管道2}  |后面可接转换方法，含.为静态调用，不含.为实例调用
              </div>
            </div>
          </template>

          <!-- AI 大模型节点属性 -->
          <template v-if="selectedNode.elementType === 'AI'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('AI')">帮助</el-button></div>
            <div class="prop-tip">大模型节点：调用已配置的大模型，把输入变量内容作为用户消息发送，回复写入输出目标；支持选择模型与图片输入（视觉识别）。</div>
            <div class="prop-item">
              <label>模型</label>
              <div style="display:flex;gap:4px">
                <el-select v-model="selectedNode.aiConfig.providerId" size="small" style="flex:1" clearable placeholder="供应商(空=第一个启用)" @change="onAiNodeProviderChange">
                  <el-option v-for="p in aiProviders" :key="p.id" :label="p.providerName" :value="p.id" />
                </el-select>
                <el-select v-model="selectedNode.aiConfig.model" size="small" style="flex:1" clearable filterable allow-create default-first-option placeholder="模型(空=默认)">
                  <el-option v-for="m in aiNodeModelOptions" :key="m" :label="m" :value="m" />
                </el-select>
              </div>
            </div>
            <div class="prop-item">
              <label>输入变量</label>
              <el-select v-model="selectedNode.aiConfig.input" size="small" style="width:100%" clearable placeholder="选择输入变量（作为用户消息）">
                <el-option-group label="流程入参">
                  <el-option v-for="p in flowInputParams" :key="'i'+p.paramCode" :value="'input_'+p.paramCode" :label="`input_${p.paramCode}`" />
                </el-option-group>
                <el-option-group label="中间变量">
                  <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
                </el-option-group>
                <el-option-group label="流程出参">
                  <el-option v-for="p in flowOutputParams" :key="'o'+p.paramCode" :value="'output_'+p.paramCode" :label="`output_${p.paramCode}`" />
                </el-option-group>
              </el-select>
            </div>
            <div class="prop-item">
              <label>图片输入</label>
              <el-select v-model="selectedNode.aiConfig.inputImages" size="small" style="width:100%" multiple collapse-tags clearable
                placeholder="可选，选择图片变量（data URL），视觉模型识别用">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
            </div>
            <div class="prop-item">
              <label>系统提示词</label>
              <el-input v-model="selectedNode.aiConfig.systemPrompt" type="textarea" :rows="4"
                placeholder="如：你是一名专业的文案专家，根据用户输入生成简洁有力的文案。" />
            </div>
            <div class="prop-item">
              <label>输出目标</label>
              <div style="display:flex;gap:4px">
                <el-select v-model="selectedNode.aiConfig.outputTargetType" size="small" style="width:90px;flex-shrink:0">
                  <el-option value="VARIABLE" label="变量" />
                  <el-option value="OUTPUT" label="出参" />
                  <el-option value="INPUT" label="入参" />
                </el-select>
                <el-select v-model="selectedNode.aiConfig.output" :placeholder="aiOutputPlaceholder(selectedNode.aiConfig.outputTargetType)" size="small" style="flex:1" clearable>
                  <template v-if="selectedNode.aiConfig.outputTargetType === 'VARIABLE'">
                    <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
                  </template>
                  <template v-else-if="selectedNode.aiConfig.outputTargetType === 'OUTPUT'">
                    <el-option v-for="p in flowOutputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                  </template>
                  <template v-else>
                    <el-option v-for="p in flowInputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                  </template>
                </el-select>
              </div>
            </div>
          </template>

          <!-- FILE_PARSE 文件解析节点属性 -->
          <template v-if="selectedNode.elementType === 'FILE_PARSE'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('FILE_PARSE')">帮助</el-button></div>
            <div class="prop-tip">文件解析节点：解析文件内容（data URL / base64 / 原文），JSON 解析为对象、CSV 解析为行数组、文本保留原文。</div>
            <div class="prop-item">
              <label>输入变量</label>
              <el-select v-model="selectedNode.fileParseConfig.input" size="small" style="width:100%" clearable placeholder="文件内容变量">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
            </div>
            <div class="prop-item">
              <label>文件类型</label>
              <el-select v-model="selectedNode.fileParseConfig.fileType" size="small" style="width:100%">
                <el-option value="auto" label="自动识别" />
                <el-option value="text" label="文本(txt/markdown)" />
                <el-option value="json" label="JSON" />
                <el-option value="xml" label="XML" />
                <el-option value="csv" label="CSV" />
                <el-option value="image" label="图片(视觉模型识别)" />
              </el-select>
            </div>
            <div class="prop-item">
              <label>输出变量</label>
              <el-select v-model="selectedNode.fileParseConfig.output" size="small" style="width:100%" clearable placeholder="解析结果变量">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
            </div>
          </template>

          <!-- EXCEL_READ Excel读取节点属性 -->
          <template v-if="selectedNode.elementType === 'EXCEL_READ'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('EXCEL_READ')">帮助</el-button></div>
            <div class="prop-tip">Excel 读取节点：读取 Excel 文件（base64 / data URL），首行为表头，返回行字典数组。</div>
            <div class="prop-item">
              <label>输入变量</label>
              <el-select v-model="selectedNode.excelReadConfig.input" size="small" style="width:100%" clearable placeholder="Excel 内容变量（base64/data URL）">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
            </div>
            <div class="prop-item">
              <label>工作表名</label>
              <el-input v-model="selectedNode.excelReadConfig.sheetName" size="small" placeholder="留空读取第一个工作表" />
            </div>
            <div class="prop-item">
              <label>输出变量</label>
              <el-select v-model="selectedNode.excelReadConfig.output" size="small" style="width:100%" clearable placeholder="行数据数组变量">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
            </div>
          </template>

          <!-- FILE_WRITE 文件写入节点属性 -->
          <template v-if="selectedNode.elementType === 'FILE_WRITE'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('FILE_WRITE')">帮助</el-button></div>
            <div class="prop-tip">文件写入节点：把变量内容生成 data URL（base64），供下载或后续节点使用；对象自动序列化为 JSON。</div>
            <div class="prop-item">
              <label>内容变量</label>
              <el-select v-model="selectedNode.fileWriteConfig.content" size="small" style="width:100%" clearable placeholder="要写入文件的内容变量">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
            </div>
            <div class="prop-item">
              <label>文件类型</label>
              <el-select v-model="selectedNode.fileWriteConfig.fileType" size="small" style="width:100%">
                <el-option value="text" label="文本(text/plain)" />
                <el-option value="json" label="JSON" />
                <el-option value="csv" label="CSV" />
              </el-select>
            </div>
            <div class="prop-item">
              <label>文件名变量</label>
              <el-select v-model="selectedNode.fileWriteConfig.fileName" size="small" style="width:100%" clearable placeholder="可选，data URL 同时写入此变量">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
            </div>
            <div class="prop-item">
              <label>输出变量</label>
              <el-select v-model="selectedNode.fileWriteConfig.output" size="small" style="width:100%" clearable placeholder="data URL 写入变量">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
            </div>
          </template>

          <!-- REDIS_GET Redis 查询节点属性 -->
          <template v-if="selectedNode.elementType === 'REDIS_GET'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('REDIS_GET')">帮助</el-button></div>
            <div class="prop-tip">Redis 查询节点：按 key 从 Redis 取值，JSON 字符串自动解析为对象写入输出变量；key 不存在时输出 null。</div>
            <div class="prop-item">
              <label>Key</label>
              <el-input v-model="selectedNode.redisGetConfig.key" size="small" placeholder="如 user:${'${input_id}'}（支持 ${'${变量}'} 模板）" />
            </div>
            <div class="prop-item">
              <label>输出变量</label>
              <el-select v-model="selectedNode.redisGetConfig.output" size="small" style="width:100%" clearable placeholder="取值写入变量">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
            </div>
          </template>

          <!-- REDIS_SET Redis 设置节点属性 -->
          <template v-if="selectedNode.elementType === 'REDIS_SET'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('REDIS_SET')">帮助</el-button></div>
            <div class="prop-tip">Redis 设置节点：写入 key-value（对象自动序列化为 JSON 字符串），可设过期秒数，结果写入输出变量。</div>
            <div class="prop-item">
              <label>Key</label>
              <el-input v-model="selectedNode.redisSetConfig.key" size="small" placeholder="如 session:${'${input_token}'}（支持 ${'${变量}'} 模板）" />
            </div>
            <div class="prop-item">
              <label>值变量</label>
              <el-select v-model="selectedNode.redisSetConfig.value" size="small" style="width:100%" clearable placeholder="写入的变量">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
            </div>
            <div class="prop-item">
              <label>过期秒数</label>
              <el-input-number v-model="selectedNode.redisSetConfig.expireSeconds" size="small" :min="0" :step="60" style="width:100%" placeholder="0=永不过期" />
            </div>
            <div class="prop-item">
              <label>输出变量</label>
              <el-select v-model="selectedNode.redisSetConfig.output" size="small" style="width:100%" clearable placeholder="是否成功(true/false)写入变量">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
            </div>
          </template>

          <!-- NOTIFY 通知节点属性 -->
          <template v-if="selectedNode.elementType === 'NOTIFY'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('NOTIFY')">帮助</el-button></div>
            <div class="prop-item">
              <label>通知类型</label>
              <el-select v-model="selectedNode.notifyConfig.notifyType" size="small" style="width:100%">
                <el-option value="WEBHOOK" label="Webhook 回调" />
                <el-option value="EMAIL" label="邮件通知" />
              </el-select>
            </div>
            <template v-if="selectedNode.notifyConfig.notifyType === 'WEBHOOK'">
              <div class="prop-item">
                <label>请求地址</label>
                <el-input v-model="selectedNode.notifyConfig.webhookUrl" size="small" placeholder="https://..." />
              </div>
              <div class="prop-item">
                <label>请求方法</label>
                <el-select v-model="selectedNode.notifyConfig.webhookMethod" size="small" style="width:100%">
                  <el-option value="POST" label="POST" />
                  <el-option value="GET" label="GET" />
                </el-select>
              </div>
              <div class="prop-item">
                <label>自定义请求头</label>
                <el-input v-model="selectedNode.notifyConfig.webhookHeaders" size="small" type="textarea" :rows="2" placeholder='{"Authorization":"Bearer xxx"}' />
              </div>
            </template>
            <template v-if="selectedNode.notifyConfig.notifyType === 'EMAIL'">
              <div class="prop-item">
                <label>收件人</label>
                <el-input v-model="selectedNode.notifyConfig.emailTo" size="small" placeholder="多个用逗号分隔" />
              </div>
              <div class="prop-item">
                <label>邮件主题</label>
                <el-input v-model="selectedNode.notifyConfig.emailSubject" size="small" placeholder="邮件主题" />
              </div>
            </template>
            <div class="prop-item">
              <label>内容模板</label>
              <el-input v-model="selectedNode.notifyConfig.bodyTemplate" size="small" type="textarea" :rows="3" placeholder="支持 ${varName} 变量替换" />
            </div>
            <div class="prop-item">
              <label>失败时中断流程</label>
              <el-switch v-model="selectedNode.notifyConfig.failOnError" size="small" />
            </div>
            <div class="prop-tip" style="margin-top:8px">通知节点在流程中发送 Webhook 回调或邮件。内容模板支持 ${varName} 变量替换。</div>
          </template>

          <!-- SUB_FLOW 子流程节点属性 -->
          <template v-if="selectedNode.elementType === 'SUB_FLOW'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('SUB_FLOW')">帮助</el-button></div>
            <div class="prop-item">
              <label>子流程 Key</label>
              <el-select v-model="selectedNode.subFlowConfig.subFlowKey" placeholder="选择已发布的子流程"
                filterable size="small" style="width:100%">
                <el-option v-for="f in publishedFlows" :key="f.flowKey" :value="f.flowKey"
                  :label="f.flowName + ' (' + f.flowKey + ')'" />
              </el-select>
            </div>

            <!-- 入参映射 -->
            <div class="prop-section-title">
              入参映射
              <el-button size="small" type="primary" link icon="Plus"
                @click="selectedNode.subFlowConfig.inputMappings.push({ source: '', sourceType: 'VARIABLE', target: '' })">
                添加
              </el-button>
            </div>
            <div v-for="(rule, idx) in selectedNode.subFlowConfig.inputMappings" :key="'sub-in-' + idx"
              class="fill-rule-row">
              <el-select v-model="rule.sourceType" size="small" style="width:88px;flex-shrink:0">
                <el-option label="变量" value="VARIABLE" />
                <el-option label="常量" value="CONSTANT" />
              </el-select>
              <el-input v-if="rule.sourceType === 'CONSTANT'" v-model="rule.source"
                placeholder="常量值" size="small" style="flex:1" />
              <el-select v-else v-model="rule.source" placeholder="来源变量" size="small"
                filterable allow-create style="flex:1">
                <el-option v-for="v in allVariables" :key="v.variableCode"
                  :value="v.variableCode" :label="v.variableCode + ' - ' + v.variableName" />
              </el-select>
              <span style="color:#999;padding:0 4px;flex-shrink:0">→</span>
              <el-select v-model="rule.target" placeholder="子流程入参名" size="small" style="flex:1"
                filterable allow-create default-first-option>
                <el-option v-for="p in subFlowInputParams" :key="p.paramCode"
                  :value="p.paramCode" :label="p.paramName + ' (' + p.paramCode + ')'" />
              </el-select>
              <el-button size="small" type="danger" link icon="Delete"
                @click="selectedNode.subFlowConfig.inputMappings.splice(idx, 1)" />
            </div>

            <!-- 出参映射 -->
            <div class="prop-section-title">
              出参映射（子流程输出 → 当前流程变量）
              <el-button size="small" type="primary" link icon="Plus"
                @click="selectedNode.subFlowConfig.outputMappings.push({ source: '', sourceType: 'VARIABLE', target: '' })">
                添加
              </el-button>
            </div>
            <div v-for="(rule, idx) in selectedNode.subFlowConfig.outputMappings" :key="'sub-out-' + idx"
              class="fill-rule-row">
              <el-select v-model="rule.source" placeholder="子流程输出变量" size="small" style="flex:1"
                filterable allow-create default-first-option>
                <el-option v-for="p in subFlowOutputParams" :key="p.paramCode"
                  :value="p.paramCode" :label="p.paramName + ' (' + p.paramCode + ')'" />
              </el-select>
              <span style="color:#999;padding:0 4px;flex-shrink:0">→</span>
              <el-select v-model="rule.target" placeholder="写入当前流程变量" size="small"
                filterable allow-create style="flex:1">
                <el-option v-for="v in allVariables" :key="v.variableCode"
                  :value="v.variableCode" :label="v.variableCode + ' - ' + v.variableName" />
              </el-select>
              <el-button size="small" type="danger" link icon="Delete"
                @click="selectedNode.subFlowConfig.outputMappings.splice(idx, 1)" />
            </div>
            <div class="prop-tip">选择要调用的子流程（所有已定义流程均可选择，调用时需确保子流程已发布）。入参映射将当前变量填入子流程，出参映射将子流程输出写回当前变量。</div>
          </template>

          <!-- METHOD 节点属性 -->
          <template v-if="selectedNode.elementType === 'METHOD'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('METHOD')">帮助</el-button></div>
            <div class="prop-item">
              <label>选择 API</label>
              <el-cascader v-model="methodApiSelection" :options="apiOptions"
                @change="onApiSelect" placeholder="选择套件/接口" size="small" style="width:100%" />
            </div>
            <div class="prop-item" v-if="selectedNode.method?.url">
              <label>URL</label>
              <el-input :value="selectedNode.method.url" disabled size="small" />
            </div>

            <!-- Header 配置 -->
            <div class="prop-section-title">
              Header 参数
              <el-button size="small" icon="Plus" link @click="addHeaderRule" style="margin-left:auto">添加</el-button>
            </div>
            <div v-for="(rule, i) in selectedNode.method?.headerFillRules" :key="'h'+i" class="fill-rule-row">
              <el-input v-model="rule.target" placeholder="Header名" size="small" style="width:40%" />
              <span class="arrow-icon">←</span>
              <el-select v-model="rule.sourceType" size="small" style="width:70px;flex-shrink:0">
                <el-option value="VARIABLE" label="变量" />
                <el-option value="CONSTANT" label="常量" />
              </el-select>
              <el-input v-if="rule.sourceType==='CONSTANT'" v-model="rule.source" placeholder="值" size="small" style="flex:1" />
              <el-select v-else v-model="rule.source" placeholder="变量" size="small" style="flex:1">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
              <el-button size="small" icon="Delete" circle type="danger" @click="selectedNode.method?.headerFillRules.splice(i, 1)" />
            </div>

            <!-- 输入参数配置 -->
            <div class="prop-section-title">
              输入参数（Body/Query）
              <el-button size="small" icon="Plus" link @click="addFillRule('input')" style="margin-left:auto">添加</el-button>
            </div>
            <div v-for="(rule, i) in selectedNode.method?.inputFillRules" :key="'i'+i">
              <div class="fill-rule-row">
                <el-select v-model="rule.sourceType" size="small" style="width:70px;flex-shrink:0" @change="rule.sourcePath = ''">
                  <el-option value="VARIABLE" label="变量" />
                  <el-option value="CONSTANT" label="常量" />
                  <el-option value="STATIC" label="静态" />
                  <el-option value="INPUT" label="入参" />
                  <el-option value="SUB_PROPERTY" label="子对象" />
                </el-select>
                <el-input v-if="rule.sourceType==='CONSTANT'" v-model="rule.source" placeholder="常量值" size="small" style="flex:1" />
                <el-select v-else-if="rule.sourceType==='STATIC'" v-model="rule.source" placeholder="选择静态变量" size="small" style="flex:1">
                  <el-option v-for="s in staticVariables" :key="s.varCode" :value="s.varCode" :label="`${s.varName} (${s.varCode})`" />
                </el-select>
                <el-select v-else-if="rule.sourceType==='INPUT'" v-model="rule.source" placeholder="选择入参" size="small" style="flex:1">
                  <el-option v-for="p in flowInputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                </el-select>
                <el-select v-else-if="rule.sourceType==='SUB_PROPERTY'" v-model="rule.source" placeholder="非简单类型" size="small" style="flex:1" @change="rule.sourcePath = ''">
                  <el-option v-for="c in allComplexTypes" :key="c.code" :value="c.code" :label="`${c.name}(${c.code}) [${sourceTypeTag(c.type)}]`" />
                </el-select>
                <el-select v-else v-model="rule.source" placeholder="来源变量" size="small" style="flex:1">
                  <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
                </el-select>
                <span class="arrow-icon">→</span>
                <el-select v-model="rule.target" placeholder="API入参名" size="small" style="width:36%" filterable allow-create default-first-option>
                  <el-option v-for="p in selectedApiInputParams" :key="(p._path || p.paramCode) + (p._prefix||'')" :value="p._path || p.paramCode" :label="`${p._displayName || p.paramName} (${p._path || p.paramCode})`" :style="{ textIndent: (p._level||0) * 16 + 'px' }" />
                </el-select>
                <el-button size="small" icon="Delete" circle type="danger" @click="selectedNode.method?.inputFillRules.splice(i, 1)" />
              </div>
              <div v-if="rule.sourceType==='SUB_PROPERTY'" class="fill-rule-row" style="margin-top:2px;">
                <span style="font-size:11px;color:#888;width:70px;flex-shrink:0">属性</span>
                <el-input v-model="rule.sourcePath" placeholder="如: name 或 user.id" size="small" style="flex:1" />
                <el-button size="small" icon="Search" @click="browseSource(rule)" style="flex-shrink:0">浏览</el-button>
              </div>
            </div>

            <!-- 输出参数配置 -->
            <div class="prop-section-title">
              输出映射（Response→变量）
              <el-button size="small" icon="Plus" link @click="addFillRule('output')" style="margin-left:auto">添加</el-button>
            </div>
            <div v-for="(rule, i) in selectedNode.method?.outputFillRules" :key="'o'+i">
              <div class="fill-rule-row">
                <el-select v-model="rule.source" placeholder="响应字段path" size="small" style="flex:1" filterable allow-create default-first-option>
                  <el-option v-for="p in selectedApiOutputParams" :key="(p._path || p.paramCode) + (p._prefix||'')" :value="p._path || p.paramCode" :label="`${p._displayName || p.paramName} (${p._path || p.paramCode})`" :style="{ textIndent: (p._level||0) * 16 + 'px' }" />
                </el-select>
                <span class="arrow-icon">→</span>
                <el-select v-model="rule.targetType" size="small" style="width:80px;flex-shrink:0">
                  <el-option value="VARIABLE" label="变量" />
                  <el-option value="OUTPUT" label="出参" />
                  <el-option value="STATIC" label="静态" />
                  <el-option value="INPUT" label="入参" />
                  <el-option value="SUB_PROPERTY" label="子对象" />
                </el-select>
                <el-select v-model="rule.target" :placeholder="methodOutputTargetPlaceholder(rule.targetType)" size="small" style="width:46%">
                  <template v-if="rule.targetType === 'VARIABLE'">
                    <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
                  </template>
                  <template v-else-if="rule.targetType === 'OUTPUT'">
                    <el-option v-for="p in flowOutputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                  </template>
                  <template v-else-if="rule.targetType === 'STATIC'">
                    <el-option v-for="s in staticVariables" :key="s.varCode" :value="s.varCode" :label="`${s.varName} (${s.varCode})`" />
                  </template>
                  <template v-else-if="rule.targetType === 'INPUT'">
                    <el-option v-for="p in flowInputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                  </template>
                  <template v-else-if="rule.targetType === 'SUB_PROPERTY'">
                    <el-option v-for="c in allComplexTypes" :key="c.code" :value="c.code" :label="`${c.name}(${c.code}) [${sourceTypeTag(c.type)}]`" />
                  </template>
                </el-select>
                <el-button size="small" icon="Delete" circle type="danger" @click="selectedNode.method?.outputFillRules.splice(i, 1)" />
              </div>
              <div v-if="rule.targetType==='SUB_PROPERTY'" class="fill-rule-row" style="margin-top:2px;">
                <span style="font-size:11px;color:#888;width:80px;flex-shrink:0">目标属性</span>
                <el-input v-model="rule.targetPath" placeholder="如: name 或 user.id" size="small" style="flex:1" />
                <el-button size="small" icon="Search" @click="browseTarget(rule)" style="flex-shrink:0">浏览</el-button>
              </div>
            </div>
          </template>

          <!-- ASSIGN 节点属性 -->
          <template v-if="selectedNode.elementType === 'ASSIGN'">
            <div class="prop-tip">赋值节点：将常量或变量赋值给目标变量。来源支持 <b>表达式</b>：算术 + - * / %、字符串拼接/切片/replace、toString 格式化，详见 <a @click="assignHelpVisible = true" style="color:#1890ff;cursor:pointer;text-decoration:underline">语法帮助</a>。</div>
            <div class="prop-section-title">
              赋值规则
              <el-button size="small" icon="QuestionFilled" link title="表达式语法帮助" @click="assignHelpVisible = true" style="margin-left:auto" />
              <el-button size="small" icon="Plus" link @click="addAssignRule">添加</el-button>
            </div>
            <div v-for="(rule, i) in selectedNode.assignRules" :key="i" class="assign-rule">
              <div class="assign-row">
                <el-select v-model="rule.sourceType" size="small" style="width:80px;flex-shrink:0" @change="onSourceTypeChange(rule)">
                  <el-option value="CONSTANT" label="常量" />
                  <el-option value="VARIABLE" label="变量" />
                  <el-option value="STATIC" label="静态" />
                  <el-option value="INPUT" label="入参" />
                  <el-option value="SUB_PROPERTY" label="子对象" />
                  <el-option value="ARRAY_OPERATION" label="数组操作" />
                  <el-option value="EXPRESSION" label="表达式" />
                </el-select>
                <template v-if="rule.sourceType === 'CONSTANT'">
                  <el-input v-model="rule.source" placeholder="常量值" size="small" style="flex:1" />
                </template>
                <template v-else-if="rule.sourceType === 'EXPRESSION'">
                  <el-input v-model="rule.source" placeholder="如: (env_price - env_cost) * env_qty 或 input_phone[..3] + '****'" size="small" style="flex:1" />
                </template>
                <template v-else-if="rule.sourceType === 'VARIABLE'">
                  <el-select v-model="rule.source" placeholder="选择变量" size="small" style="flex:1">
                    <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
                  </el-select>
                </template>
                <template v-else-if="rule.sourceType === 'STATIC'">
                  <el-select v-model="rule.source" placeholder="选择静态变量" size="small" style="flex:1">
                    <el-option v-for="s in staticVariables" :key="s.varCode" :value="s.varCode" :label="`${s.varName} (${s.varCode})`" />
                  </el-select>
                </template>
                <template v-else-if="rule.sourceType === 'INPUT'">
                  <el-select v-model="rule.source" placeholder="选择入参" size="small" style="flex:1">
                    <el-option v-for="p in flowInputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                  </el-select>
                </template>
                <template v-else-if="rule.sourceType === 'SUB_PROPERTY'">
                  <el-select v-model="rule.source" placeholder="选择非简单类型" size="small" style="flex:1" @change="rule.sourcePath = ''">
                    <el-option v-for="c in allComplexTypes" :key="c.code" :value="c.code" :label="`${c.name}(${c.code}) [${sourceTypeTag(c.type)}]`" />
                  </el-select>
                </template>
                <template v-else-if="rule.sourceType === 'ARRAY_OPERATION'">
                  <el-select v-model="rule.source" placeholder="选择数组" size="small" style="flex:1" @change="rule.sourcePath = ''; rule.arrayOpType = ''">
                    <el-option v-for="a in allArrayTypes" :key="a.code" :value="a.code" :label="`${a.name}(${a.code}) [${sourceTypeTag(a.type)}]`" />
                  </el-select>
                </template>
              </div>
              <!-- 源属性路径行 -->
              <div v-if="rule.sourceType === 'SUB_PROPERTY'" class="assign-row" style="margin-top:2px;">
                <span style="font-size:11px;color:#888;width:80px;flex-shrink:0">源属性</span>
                <el-input v-model="rule.sourcePath" placeholder="如: name 或 user.id" size="small" style="flex:1" />
                <el-button size="small" icon="Search" @click="browseSource(rule)" style="flex-shrink:0">浏览</el-button>
              </div>
              <!-- 源数组操作行 -->
              <div v-if="rule.sourceType === 'ARRAY_OPERATION'" class="assign-row" style="margin-top:2px;">
                <span style="font-size:11px;color:#888;width:80px;flex-shrink:0">数组操作</span>
                <el-select v-model="rule.arrayOpType" placeholder="操作方式" size="small" style="width:90px;flex-shrink:0">
                  <el-option value="PAGINATE" label="分页" />
                  <el-option value="GET_INDEX" label="取第n个" />
                  <el-option value="TO_JSON" label="转JSON" />
                </el-select>
                <template v-if="rule.arrayOpType === 'PAGINATE'">
                  <span style="font-size:11px;color:#666">页</span>
                  <el-input v-model="rule.arrayOpPageNum" placeholder="0" size="small" style="width:60px" />
                  <span style="font-size:11px;color:#666">每页</span>
                  <el-input v-model="rule.arrayOpPageSize" placeholder="10" size="small" style="width:60px" />
                </template>
                <template v-else-if="rule.arrayOpType === 'GET_INDEX'">
                  <span style="font-size:11px;color:#666">索引</span>
                  <el-input v-model="rule.arrayOpIndex" placeholder="0" size="small" style="width:80px" />
                </template>
              </div>
              <div class="assign-row" style="margin-top:4px">
                <span style="font-size:12px;color:#666;width:72px;flex-shrink:0">→ 赋值给</span>
                <el-select v-model="rule.targetType" size="small" style="width:80px;flex-shrink:0" @change="onTargetTypeChange(rule)">
                  <el-option value="VARIABLE" label="变量" />
                  <el-option value="OUTPUT" label="出参" />
                  <el-option value="STATIC" label="静态" />
                  <el-option value="INPUT" label="入参" />
                  <el-option value="SUB_PROPERTY" label="子对象" />
                  <el-option value="ARRAY_OPERATION" label="数组操作" />
                </el-select>
                <el-select v-model="rule.target" :placeholder="getTargetPlaceholder(rule.targetType)" size="small" style="flex:1">
                  <template v-if="rule.targetType === 'VARIABLE'">
                    <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
                  </template>
                  <template v-else-if="rule.targetType === 'OUTPUT'">
                    <el-option v-for="p in flowOutputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                  </template>
                  <template v-else-if="rule.targetType === 'STATIC'">
                    <el-option v-for="s in staticVariables" :key="s.varCode" :value="s.varCode" :label="`${s.varName} (${s.varCode})`" />
                  </template>
                  <template v-else-if="rule.targetType === 'INPUT'">
                    <el-option v-for="p in flowInputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                  </template>
                  <template v-else-if="rule.targetType === 'SUB_PROPERTY'">
                    <el-option v-for="c in allComplexTypes" :key="c.code" :value="c.code" :label="`${c.name}(${c.code}) [${sourceTypeTag(c.type)}]`" />
                  </template>
                  <template v-else-if="rule.targetType === 'ARRAY_OPERATION'">
                    <el-option v-for="a in allArrayTypes" :key="a.code" :value="a.code" :label="`${a.name}(${a.code}) [${sourceTypeTag(a.type)}]`" />
                  </template>
                </el-select>
                <el-select v-if="rule.targetType !== 'ARRAY_OPERATION'" v-model="rule.dataType" size="small" style="width:72px;flex-shrink:0">
                  <el-option value="string" label="string" />
                  <el-option value="integer" label="integer" />
                  <el-option value="double" label="double" />
                  <el-option value="boolean" label="bool" />
                </el-select>
                <el-button size="small" icon="Delete" circle type="danger" @click="selectedNode.assignRules.splice(i, 1)" />
              </div>
              <!-- 目标属性路径行 -->
              <div v-if="rule.targetType === 'SUB_PROPERTY'" class="assign-row" style="margin-top:2px;">
                <span style="font-size:11px;color:#888;width:80px;flex-shrink:0">目标属性</span>
                <el-input v-model="rule.targetPath" placeholder="如: name 或 user.id" size="small" style="flex:1" />
                <el-button size="small" icon="Search" @click="browseTarget(rule)" style="flex-shrink:0">浏览</el-button>
              </div>
              <!-- 目标数组操作行 -->
              <div v-if="rule.targetType === 'ARRAY_OPERATION'" class="assign-row" style="margin-top:2px;">
                <span style="font-size:11px;color:#888;width:80px;flex-shrink:0">数组操作</span>
                <el-select v-model="rule.arrayOpType" placeholder="操作方式" size="small" style="width:90px;flex-shrink:0">
                  <el-option value="PAGINATE" label="分页" />
                  <el-option value="GET_INDEX" label="取第n个" />
                  <el-option value="TO_JSON" label="转JSON" />
                </el-select>
                <template v-if="rule.arrayOpType === 'PAGINATE'">
                  <span style="font-size:11px;color:#666">页</span>
                  <el-input v-model="rule.arrayOpPageNum" placeholder="0" size="small" style="width:60px" />
                  <span style="font-size:11px;color:#666">每页</span>
                  <el-input v-model="rule.arrayOpPageSize" placeholder="10" size="small" style="width:60px" />
                </template>
                <template v-else-if="rule.arrayOpType === 'GET_INDEX'">
                  <span style="font-size:11px;color:#666">索引</span>
                  <el-input v-model="rule.arrayOpIndex" placeholder="0" size="small" style="width:80px" />
                </template>
              </div>
            </div>
          </template>

          <!-- CODE 节点属性 -->
          <template v-if="selectedNode.elementType === 'CODE'">
            <div style="display:flex;justify-content:flex-end"><el-button size="small" icon="QuestionFilled" link @click="openNodeHelp('CODE')">帮助</el-button></div>
            <div class="prop-tip">
              代码节点：编写 JavaScript 脚本操作变量。<br>
              读取：<code>$var.getVariableValue('key')</code><br>
              写入：<code>$var.setVariableValue('key', val)</code>
            </div>
            <div class="prop-item">
              <label>脚本语言</label>
              <el-select v-model="selectedNode.codeConfig.scriptType" size="small" style="width:120px">
                <el-option value="javascript" label="JavaScript" />
              </el-select>
            </div>
            <div class="prop-item">
              <label>脚本内容</label>
              <MonacoEditor
                v-model="selectedNode.codeConfig.script"
                language="javascript"
                theme="vs-dark"
                height="240px"
                style="border-radius:4px;overflow:hidden"
              />
            </div>
          </template>

          <!-- MYSQL/DB 节点属性 -->
          <template v-if="selectedNode.elementType === 'MYSQL'">
            <div class="prop-tip">数据库节点：执行 SQL，支持 <code>${varName}</code> 模板变量。可浏览数据源表结构辅助生成 SQL、单独测试 SQL，详见 <a @click="dbHelpVisible = true" style="color:#1890ff;cursor:pointer;text-decoration:underline">语法帮助</a>。</div>
            <div class="prop-item">
              <label>数据源</label>
              <el-select v-model="selectedNode.mysqlConfig.dataSourceName" placeholder="选择数据源" size="small" style="width:100%">
                <el-option v-for="ds in dataSources" :key="ds.id" :value="ds.dataSourceName"
                  :label="`${ds.dataSourceName} (${ds.dataSourceType})`" />
              </el-select>
            </div>
            <div class="prop-item">
              <label>操作类型</label>
              <el-radio-group v-model="selectedNode.mysqlConfig.operationType" size="small">
                <el-radio-button value="QUERY">查询</el-radio-button>
                <el-radio-button value="UPDATE">更改</el-radio-button>
              </el-radio-group>
            </div>
            <div class="prop-item">
              <label>SQL 语句</label>
              <MonacoEditor
                v-model="selectedNode.mysqlConfig.sql"
                language="sql"
                theme="vs-dark"
                height="160px"
                style="border-radius:4px;overflow:hidden"
              />
              <div style="display:flex;gap:6px;margin-top:6px;align-items:center">
                <el-button size="small" icon="Collection" @click="openDbObjectDialog">表/视图/存储过程</el-button>
                <el-button size="small" icon="VideoPlay" type="primary" plain @click="openDbTestDialog">测试 SQL</el-button>
                <el-button size="small" icon="QuestionFilled" link title="SQL 编写帮助" @click="dbHelpVisible = true" style="margin-left:auto" />
              </div>
            </div>
            <div class="prop-item" v-if="selectedNode.mysqlConfig.operationType === 'QUERY'">
              <label>查询结果写入</label>
              <div style="display:flex;gap:4px">
                <el-select v-model="selectedNode.mysqlConfig.outputTargetType" size="small" style="width:80px;flex-shrink:0">
                  <el-option value="VARIABLE" label="变量" />
                  <el-option value="OUTPUT" label="出参" />
                </el-select>
                <el-select v-model="selectedNode.mysqlConfig.outputVariable" :placeholder="selectedNode.mysqlConfig.outputTargetType === 'VARIABLE' ? '选择变量' : '选择输出参数'" size="small" style="flex:1" clearable>
                  <template v-if="selectedNode.mysqlConfig.outputTargetType === 'VARIABLE'">
                    <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
                  </template>
                  <template v-else-if="selectedNode.mysqlConfig.outputTargetType === 'OUTPUT'">
                    <el-option v-for="p in flowOutputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                  </template>
                </el-select>
              </div>
            </div>
            <div class="prop-item" v-else>
              <label>影响行数写入</label>
              <div style="display:flex;gap:4px">
                <el-select v-model="selectedNode.mysqlConfig.affectedTargetType" size="small" style="width:80px;flex-shrink:0">
                  <el-option value="VARIABLE" label="变量" />
                  <el-option value="OUTPUT" label="出参" />
                </el-select>
                <el-select v-model="selectedNode.mysqlConfig.affectedRowsVariable" :placeholder="selectedNode.mysqlConfig.affectedTargetType === 'VARIABLE' ? '选择变量' : '选择输出参数'" size="small" style="flex:1" clearable>
                  <template v-if="selectedNode.mysqlConfig.affectedTargetType === 'VARIABLE'">
                    <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
                  </template>
                  <template v-else-if="selectedNode.mysqlConfig.affectedTargetType === 'OUTPUT'">
                    <el-option v-for="p in flowOutputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                  </template>
                </el-select>
              </div>
            </div>
          </template>

          <!-- CONDITION 节点属性 -->
          <template v-if="selectedNode.elementType === 'CONDITION'">
            <div class="prop-tip">条件节点：每个分支连接到不同的目标节点（通过画布连线），在此设置判断表达式。支持 && || 括号、+ - * / % 算术运算、子属性/数组取值、字符串拼接切片与 toString 格式化，详见 <a @click="conditionHelpVisible = true" style="color:#1890ff;cursor:pointer;text-decoration:underline">语法帮助</a>。</div>
            <div class="prop-section-title">
              条件分支
              <el-button size="small" icon="QuestionFilled" link title="条件语法帮助" @click="conditionHelpVisible = true" style="margin-left:auto" />
              <el-button size="small" icon="Plus" link @click="addCondition">添加</el-button>
            </div>
            <div v-for="(cond, i) in selectedNode.conditions" :key="i" class="condition-item">
              <div style="display:flex;gap:4px;align-items:center;margin-bottom:4px">
                <el-input v-model="cond.conditionName" placeholder="分支名称" size="small" style="flex:1" />
                <el-select v-model="cond.conditionType" size="small" style="width:90px;flex-shrink:0">
                  <el-option value="CUSTOM" label="自定义" />
                  <el-option value="DEFAULT" label="默认(else)" />
                </el-select>
                <el-button size="small" icon="Delete" circle type="danger" @click="selectedNode.conditions.splice(i, 1)" />
              </div>
              <el-input v-if="cond.conditionType === 'CUSTOM'"
                v-model="cond.expression"
                placeholder="如: env_score >= 60 && (env_score < 90 || env_flag)"
                size="small" style="margin-bottom:4px" />
              <div style="display:flex;align-items:center;gap:4px">
                <span style="font-size:12px;color:#666;white-space:nowrap;flex-shrink:0">跳转→</span>
                <el-select v-model="cond.outgoing" placeholder="下一节点（或从画布连线）" size="small" style="flex:1" clearable>
                  <el-option v-for="n in otherNodes" :key="n.key" :value="n.key"
                    :label="`${nodeTypeName(n.elementType)}: ${n.label || n.key}`" />
                </el-select>
              </div>
            </div>
          </template>

          <!-- 通用配置：超时 + 重试（非 START/END/MERGE/CONDITION/LOOP/DELAY/PARALLEL 节点） -->
          <template v-if="!['START','END','MERGE','CONDITION','LOOP','DELAY','PARALLEL','NOTIFY'].includes(selectedNode.elementType)">
            <div class="prop-section-title" style="margin-top:12px">⏱ 超时 / 重试</div>
            <div class="prop-item">
              <label>超时时间(ms)</label>
              <el-input-number v-model="selectedNode.timeout" :min="0" :step="1000" size="small" style="width:100%"
                placeholder="0=不限" />
            </div>
            <div class="prop-item">
              <label>失败重试次数</label>
              <el-input-number v-model="selectedNode.retryCount" :min="0" :max="10" size="small" style="width:100%"
                placeholder="0=不重试" />
            </div>
            <div class="prop-item" v-if="selectedNode.retryCount > 0">
              <label>重试间隔(ms)</label>
              <el-input-number v-model="selectedNode.retryInterval" :min="200" :step="500" size="small" style="width:100%" />
            </div>
          </template>

          <!-- 连线提示（所有节点通用） -->
          <div class="prop-tip" style="margin-top:12px;background:#e6f4ff;color:#1890ff">
            💡 <b>连线方式：</b>拖动节点底部蓝色连接点到目标节点顶部，即可建立连线。也可直接在画布上拖动节点改变位置。
          </div>
        </div>
        <el-empty v-else description="点击画布中的节点查看/编辑属性" :image-size="60" style="padding-top:40px" />
      </div>
    </div>

    <!-- ========== 流程参数抽屉（入参/出参） ========== -->
    <el-drawer v-model="paramDrawer" title="📋 流程参数配置" size="640px" direction="rtl">
      <el-tabs v-model="paramTab" style="padding:0 8px">
        <el-tab-pane label="入参（Input）" name="input">
          <div style="margin-bottom:8px;display:flex;gap:8px;justify-content:flex-end">
            <el-button size="small" icon="Plus" @click="addFlowParam('input')">添加入参</el-button>
            <el-button size="small" icon="CollectionTag" @click="openFlowObjectDialog('input')">来自对象</el-button>
          </div>
          <el-table :data="flowInputParams" border size="small" empty-text="暂无入参">
            <el-table-column type="index" width="42" label="#" />
            <el-table-column label="参数Code" width="140">
              <template #default="{ row }">
                <el-input v-model="row.paramCode" size="small" placeholder="input_xxx" />
              </template>
            </el-table-column>
            <el-table-column label="参数名" width="100">
              <template #default="{ row }">
                <el-input v-model="row.paramName" size="small" />
              </template>
            </el-table-column>
            <el-table-column label="类型" width="85">
              <template #default="{ row }">
                <el-select v-model="row.dataType" size="small" style="width:100%" @change="onFlowParamTypeChange(row)">
                  <el-option v-for="t in dataTypes" :key="t.value" :value="t.value" :label="t.label" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column v-if="hasObjectParams('input')" label="关联对象" width="120">
              <template #default="{ row }">
                <el-select v-if="row.dataType === 'object' || row.dataType === 'array'" v-model="row.objectCode" size="small" style="width:100%" clearable @change="onFlowParamObjChange(row, $event)">
                  <el-option v-for="obj in objectList" :key="obj.id" :label="obj.objectName" :value="obj.objectCode" />
                </el-select>
                <span v-else style="color:#ccc;font-size:11px">—</span>
              </template>
            </el-table-column>
            <el-table-column label="必填" width="50" align="center">
              <template #default="{ row }">
                <el-checkbox v-model="row.required" :true-value="1" :false-value="0" />
              </template>
            </el-table-column>
            <el-table-column label="默认值" width="80">
              <template #default="{ row }">
                <el-input v-model="row.defaultValue" size="small" />
              </template>
            </el-table-column>
            <el-table-column label="描述">
              <template #default="{ row }">
                <el-input v-model="row.description" size="small" />
              </template>
            </el-table-column>
            <el-table-column label="" width="45" align="center">
              <template #default="{ $index }">
                <el-button size="small" type="danger" link @click="flowInputParams.splice($index,1)">删</el-button>
              </template>
            </el-table-column>
          </el-table>
          <div style="margin-top:12px;text-align:right">
            <el-button type="primary" @click="saveFlowParams('input')">保存入参</el-button>
          </div>
        </el-tab-pane>

        <el-tab-pane label="出参（Output）" name="output">
          <div style="margin-bottom:8px;display:flex;gap:8px;justify-content:flex-end">
            <el-button size="small" icon="Plus" @click="addFlowParam('output')">添加出参</el-button>
            <el-button size="small" icon="CollectionTag" @click="openFlowObjectDialog('output')">来自对象</el-button>
          </div>
          <el-table :data="flowOutputParams" border size="small" empty-text="暂无出参">
            <el-table-column type="index" width="42" label="#" />
            <el-table-column label="参数Code" width="140">
              <template #default="{ row }">
                <el-input v-model="row.paramCode" size="small" placeholder="output_xxx" />
              </template>
            </el-table-column>
            <el-table-column label="参数名" width="100">
              <template #default="{ row }">
                <el-input v-model="row.paramName" size="small" />
              </template>
            </el-table-column>
            <el-table-column label="类型" width="85">
              <template #default="{ row }">
                <el-select v-model="row.dataType" size="small" style="width:100%" @change="onFlowParamTypeChange(row)">
                  <el-option v-for="t in dataTypes" :key="t.value" :value="t.value" :label="t.label" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column v-if="hasObjectParams('output')" label="关联对象" width="120">
              <template #default="{ row }">
                <el-select v-if="row.dataType === 'object' || row.dataType === 'array'" v-model="row.objectCode" size="small" style="width:100%" clearable>
                  <el-option v-for="obj in objectList" :key="obj.id" :label="obj.objectName" :value="obj.objectCode" />
                </el-select>
                <span v-else style="color:#ccc;font-size:11px">—</span>
              </template>
            </el-table-column>
            <el-table-column label="描述">
              <template #default="{ row }">
                <el-input v-model="row.description" size="small" />
              </template>
            </el-table-column>
            <el-table-column label="" width="45" align="center">
              <template #default="{ $index }">
                <el-button size="small" type="danger" link @click="flowOutputParams.splice($index,1)">删</el-button>
              </template>
            </el-table-column>
          </el-table>
          <div style="margin-top:12px;text-align:right">
            <el-button type="primary" @click="saveFlowParams('output')">保存出参</el-button>
          </div>
        </el-tab-pane>
      </el-tabs>

      <!-- 来自对象对话框（参数抽屉内） -->
      <el-dialog v-model="flowObjDialogVisible" :title="`选择对象 — 导入${flowObjDialogType === 'input' ? '入参' : '出参'}`" width="480px" append-to-body>
        <el-form label-width="80px">
          <el-form-item label="选择对象">
            <el-select v-model="selectedFlowObjId" placeholder="请选择对象类型" style="width:100%" @change="onFlowObjSelect">
              <el-option v-for="obj in objectList" :key="obj.id" :label="`${obj.objectName} (${obj.objectCode})`" :value="obj.id" />
            </el-select>
          </el-form-item>
        </el-form>
        <el-divider v-if="flowObjPreviewParams.length > 0" />
        <el-table v-if="flowObjPreviewParams.length > 0" :data="flowObjPreviewParams" border size="small" max-height="300">
          <el-table-column prop="paramCode" label="参数Code" />
          <el-table-column prop="paramName" label="参数名" />
          <el-table-column label="数据类型" width="100">
            <template #default="{ row }">
              <el-tag size="small">{{ row.dataType }}</el-tag>
            </template>
          </el-table-column>
        </el-table>
        <template #footer>
          <el-button @click="flowObjDialogVisible = false">取消</el-button>
          <el-button type="primary" :disabled="flowObjPreviewParams.length === 0" @click="importFlowObjParams">导入到参数列表</el-button>
        </template>
      </el-dialog>

    </el-drawer>

    <!-- ========== 变量管理抽屉 ========== -->
    <el-drawer v-model="variableDrawer" title="🔧 流程变量管理" size="520px" direction="rtl">
      <div style="padding:0 4px">
        <div style="margin-bottom:12px;display:flex;justify-content:space-between;align-items:center">
          <span style="color:#666;font-size:13px">定义流程中间变量（运行时上下文），在节点填充规则中引用。入参和出参请在「流程参数」中配置。</span>
          <el-button size="small" type="primary" icon="Plus" @click="addVariable">添加变量</el-button>
        </div>
        <el-table :data="allVariables" border size="small">
          <el-table-column label="变量Code" width="140">
            <template #default="{ row }">
              <el-input v-model="row.variableCode" size="small" placeholder="如: env_result" />
            </template>
          </el-table-column>
          <el-table-column label="变量名" width="110">
            <template #default="{ row }">
              <el-input v-model="row.variableName" size="small" placeholder="如: 结果" />
            </template>
          </el-table-column>
          <el-table-column label="类型" width="80">
            <template #default>
              <el-tag size="small" type="info">中间</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="数据类型" width="90">
            <template #default="{ row }">
              <el-select v-model="row.dataType" size="small" style="width:100%" @change="onVarTypeChange(row)">
                <el-option v-for="t in dataTypes" :key="t.value" :value="t.value" :label="t.label" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column v-if="hasObjectVars" label="关联对象" width="120">
            <template #default="{ row }">
              <el-select v-if="row.dataType === 'object' || row.dataType === 'array'" v-model="row.objectCode" size="small" style="width:100%" clearable>
                <el-option v-for="obj in objectList" :key="obj.id" :label="obj.objectName" :value="obj.objectCode" />
              </el-select>
              <span v-else style="color:#ccc;font-size:11px">—</span>
            </template>
          </el-table-column>
          <el-table-column label="默认值">
            <template #default="{ row }">
              <el-input v-model="row.defaultValue" size="small" placeholder="可选" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="50">
            <template #default="{ $index }">
              <el-button size="small" type="danger" link @click="allVariables.splice($index, 1)">删</el-button>
            </template>
          </el-table-column>
        </el-table>
        <div style="margin-top:16px;text-align:right">
          <el-button type="primary" @click="saveVariables">保存变量</el-button>
        </div>
      </div>

      <el-dialog v-model="varDialogVisible" title="添加变量" width="420px" append-to-body>
        <el-form :model="varForm" label-width="80px" size="small">
          <el-form-item label="变量Code">
            <el-input v-model="varForm.variableCode" placeholder="如: input_city" />
          </el-form-item>
          <el-form-item label="变量名">
            <el-input v-model="varForm.variableName" placeholder="如: 城市名称" />
          </el-form-item>
          <el-form-item label="类型">
            <el-select v-model="varForm.variableType" style="width:100%" disabled>
              <el-option value="VARIABLE" label="中间变量" />
            </el-select>
          </el-form-item>
          <el-form-item label="数据类型">
            <el-select v-model="varForm.dataType" style="width:100%">
              <el-option v-for="t in dataTypes" :key="t.value" :value="t.value" :label="t.label" />
            </el-select>
          </el-form-item>
          <el-form-item label="默认值">
            <el-input v-model="varForm.defaultValue" placeholder="可选" />
          </el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="varDialogVisible = false">取消</el-button>
          <el-button type="primary" @click="confirmAddVariable">确认</el-button>
        </template>
      </el-dialog>
    </el-drawer>

    <!-- 属性浏览器对话框 -->
    <el-dialog v-model="propBrowserVisible" title="选择对象属性" width="620px" append-to-body>
      <div style="display:flex;gap:12px;overflow-x:auto;min-height:200px">
        <div v-for="(level, lIdx) in propBrowserLevels" :key="lIdx" style="min-width:180px;flex-shrink:0">
          <div style="font-size:12px;color:#999;margin-bottom:4px">
            {{ lIdx === 0 ? '选择属性' : '子属性' }}
            <el-tag v-if="level.objectCode" size="small" style="margin-left:4px">{{ level.objectCode }}</el-tag>
          </div>
          <div v-if="!level.params || level.params.length === 0" style="color:#ccc;font-size:13px;padding:8px">
            {{ level.label || '无可用属性' }}
          </div>
          <el-radio-group v-else v-model="level.selected" @change="onPropLevelSelect(lIdx, $event)" style="display:flex;flex-direction:column;width:100%;row-gap:4px">
            <el-radio v-for="p in level.params" :key="p.paramCode" :value="p.paramCode" style="padding:4px 8px;border:1px solid #eee;border-radius:4px;display:flex;align-items:flex-start;text-align:left;width:100%;margin-right:0">
              <span>{{ p.paramName || p.paramCode }}</span>
              <el-tag size="small" style="margin-left:4px;flex-shrink:0">{{ p.dataType || 'string' }}</el-tag>
            </el-radio>
          </el-radio-group>
        </div>
      </div>
      <div v-if="propBrowserLevels.length > 0 && propBrowserLevels.some(l => l.selected)" style="margin-top:8px;color:#1890ff;font-size:12px">
        已选路径: {{ propBrowserLevels.filter(l => l.selected).map(l => l.selected).join(' → ') }}
      </div>
      <template #footer>
        <el-button @click="propBrowserVisible = false">取消</el-button>
        <el-button type="primary" @click="confirmPropSelection">确定</el-button>
      </template>
    </el-dialog>

    <!-- ========== 调试弹窗 ========== -->
    <el-dialog v-model="debugVisible" title="🐛 流程调试" width="780px" :close-on-click-modal="false" :modal="false" draggable>
      <div style="margin-bottom:8px;color:#666;font-size:13px">
        已定义的入参：
        <el-tag v-for="p in flowInputParams" :key="p.paramCode" size="small" style="margin-right:4px">
          {{ p.paramCode }}[{{ p.paramName }}]({{ p.dataType }})
        </el-tag>
        <span v-if="!flowInputParams.length" style="color:#aaa">无</span>
      </div>
      <el-form label-width="100px">
        <el-form-item label="输入参数">
          <el-input v-model="debugParams" type="textarea" :rows="5"
            placeholder='{"input_city": "北京", "input_name": "张三"}' class="code-editor" />
        </el-form-item>
      </el-form>

      <div v-if="debugResult !== null" style="margin-top:8px">
        <el-divider />
        <!-- 执行状态 -->
        <div style="display:flex;align-items:center;gap:12px;margin-bottom:10px">
          <span style="font-weight:bold;font-size:15px" :style="{ color: debugResult.success ? '#52c41a' : '#ff4d4f' }">
            {{ debugResult.success ? '✅ 执行成功' : '❌ 执行失败' }}
          </span>
          <span v-if="debugResult.costMs || debugResult.executionTime" style="color:#888;font-size:12px">
            耗时 {{ debugResult.costMs ?? debugResult.executionTime }} ms
          </span>
          <el-button size="small" @click="clearDebugHighlight" v-if="hasDebugHighlight">清除高亮</el-button>
        </div>

        <!-- 失败时显示错误信息 -->
        <el-alert
          v-if="!debugResult.success && debugResult.errorMessage"
          :title="debugResult.errorMessage"
          type="error"
          show-icon
          :closable="false"
          style="margin-bottom:10px"
        />

        <el-tabs v-model="debugTab">
          <!-- 节点执行时间轴 -->
          <el-tab-pane label="🔍 节点执行详情" name="timeline">
            <div v-if="debugResult.nodeLogs && debugResult.nodeLogs.length" style="max-height:300px;overflow-y:auto;padding:4px 0">
              <div v-for="(log, idx) in debugResult.nodeLogs" :key="idx"
                class="debug-timeline-item"
                :class="log.status === 'SUCCESS' ? 'dtl-success' : 'dtl-fail'"
                @click="showNodeDebugDetail(log.nodeKey)"
              >
                <div class="dtl-seq">{{ getLogSeq(log, idx) }}</div>
                <div class="dtl-body">
                  <div class="dtl-header">
                    <span class="dtl-icon">{{ nodeIcon(log.nodeType || '') }}</span>
                    <span class="dtl-key">{{ log.nodeKey }}</span>
                    <el-tag size="small" :type="log.status === 'SUCCESS' ? 'success' : 'danger'" style="margin-left:6px">
                      {{ log.status === 'SUCCESS' ? '✓ 成功' : '✗ 失败' }}
                    </el-tag>
                    <span v-if="log.executionTime" style="color:#aaa;font-size:11px;margin-left:auto">{{ log.executionTime }}ms</span>
                  </div>
                  <!-- 节点失败时显示错误信息（errorMessage 优先，其次 detail） -->
                  <div v-if="log.status !== 'SUCCESS'" class="dtl-error">
                    {{ log.errorMessage || log.detail || '节点执行失败' }}
                  </div>
                </div>
              </div>
            </div>
            <el-empty v-else description="暂无节点执行记录" :image-size="40" />
          </el-tab-pane>

          <!-- 输出结果 -->
          <el-tab-pane label="📤 输出结果" name="output">
            <el-input :value="debugOutputStr" type="textarea" :rows="8" readonly class="code-editor" />
          </el-tab-pane>

          <!-- 完整JSON -->
          <el-tab-pane label="📋 完整JSON" name="raw">
            <el-input v-model="debugResultStr" type="textarea" :rows="8" readonly class="code-editor" />
          </el-tab-pane>
        </el-tabs>
      </div>

      <template #footer>
        <el-button @click="debugVisible = false">关闭</el-button>
        <el-button type="primary" @click="runDebug" :loading="debugLoading">执行</el-button>
      </template>
    </el-dialog>

    <!-- 节点调试详情弹窗 -->
    <el-dialog v-model="nodeDebugDetailVisible" :title="`节点调试详情：${nodeDebugDetailKey}`" width="620px" append-to-body>
      <el-tabs>
        <el-tab-pane label="输入变量快照">
          <el-input :value="nodeDebugInputStr" type="textarea" :rows="8" readonly class="code-editor" />
        </el-tab-pane>
        <el-tab-pane label="输出变量快照">
          <el-input :value="nodeDebugOutputStr" type="textarea" :rows="8" readonly class="code-editor" />
        </el-tab-pane>
        <el-tab-pane label="详细信息">
          <el-input :value="nodeDebugDetailStr" type="textarea" :rows="8" readonly class="code-editor" />
        </el-tab-pane>
      </el-tabs>
    </el-dialog>

    <!-- 条件表达式语法帮助弹窗 -->
    <el-dialog v-model="conditionHelpVisible" title="❓ 条件表达式语法帮助" width="660px" append-to-body>
      <div class="condition-help">
        <div class="ch-section">一、基本比较</div>
        <pre>env_score &gt;= 60
input_name == '张三'        <span class="ch-note">// 字符串用单引号或双引号包裹</span>
env_status != 'deleted'</pre>
        <div class="ch-section">二、逻辑运算：&amp;&amp;（且） ||（或） !（非） 括号</div>
        <pre>env_score &gt;= 60 &amp;&amp; env_score &lt; 90
env_status == 'paid' || env_status == 'done'
!(env_deleted)              <span class="ch-note">// 取反，等价于 env_deleted == false</span>
(env_a &gt; 1 || env_b &gt; 2) &amp;&amp; env_c == 'yes'</pre>
        <div class="ch-section">三、左右两边都可以是变量</div>
        <pre>env_total &gt;= input_min
input_a == env_b</pre>
        <div class="ch-section">四、子属性访问</div>
        <pre>input_user.name == '张三'
env_order.amount &gt;= 100</pre>
        <div class="ch-section">五、数组：长度 / 指定元素 / 元素子属性（索引从 0 开始）</div>
        <pre>input_list.length &gt; 0                  <span class="ch-note">// 数组长度</span>
input_list[0].name == '张三'            <span class="ch-note">// 第 1 个元素</span>
env_orders[2].status == 'paid'          <span class="ch-note">// 第 3 个元素</span>
input_list.length &gt; 2 &amp;&amp; input_list[2].price &lt; env_limit</pre>
        <div class="ch-section">六、算术运算：+ - * / % （可先运算再比较）</div>
        <pre>env_a + env_b &gt; 10
(env_price - env_cost) * env_qty &gt;= 100
env_total % 2 == 0                     <span class="ch-note">// 取余</span>
env_amount / 3 &gt; 1
env_a * 2 &lt; env_b + 5</pre>
        <div class="ch-note">+ 两边都是数字时做加法，否则做字符串拼接；* 会把数字字符串转为数字计算。</div>
        <div class="ch-section">七、字符串操作：拼接 / 切片 / 替换</div>
        <pre>env_name + '_done' == 'abc_done'       <span class="ch-note">// + 拼接</span>
input_phone[..3] == '138'               <span class="ch-note">// 前 3 位（[..5] 取前 5 位）</span>
env_code[2..5] == 'abc'                 <span class="ch-note">// 第 2~5 位（不含第 5 位）</span>
env_code[2..] == 'cde'                  <span class="ch-note">// 从第 2 位到最后</span>
env_name.replace('a','b') == 'xby'      <span class="ch-note">// 替换</span>
env_code[0] == 'A'                      <span class="ch-note">// 取单个字符</span></pre>
        <div class="ch-section">八、toString 格式化：数字 / 日期</div>
        <pre>env_amount.toString('#.0##') == '12.35'              <span class="ch-note">// 数字格式 #.0##</span>
env_amount.toString('0.00') == '12.35'                <span class="ch-note">// 保留 2 位小数</span>
env_time.toString('yyyy-MM-dd HH:mm:ss') == '2024-01-01 10:00:00'   <span class="ch-note">// 日期格式</span>
env_time.toString('yyyy-MM-dd') == '2024-01-01'</pre>
        <div class="ch-note" style="margin-top:8px">
          变量命名：input_ 流程入参、output_ 流程出参、env_ 中间变量、_loop_item 循环项。表达式不写比较符时按真值判断（如 env_flag 等价于 env_flag == true）。字符串请用引号包裹（如 'paid'），未加引号的词若不存在同名变量也会按字符串处理。
        </div>
      </div>
    </el-dialog>

    <!-- 赋值节点：表达式语法帮助弹窗 -->
    <el-dialog v-model="assignHelpVisible" title="❓ 赋值节点表达式语法帮助" width="660px" append-to-body>
      <div class="condition-help">
        <div class="ch-section">一、算术运算：+ - * / % （支持括号）</div>
        <pre>(env_price - env_cost) * env_qty          <span class="ch-note">// 利润 × 数量</span>
env_total * 1.1                            <span class="ch-note">// 加 10%</span>
env_total % 3                              <span class="ch-note">// 取余</span>
100 * 2 + 5                                <span class="ch-note">// 纯字面量运算</span>
env_a + env_b * 2                          <span class="ch-note">// 先乘除后加减</span></pre>
        <div class="ch-note">+ 两边都是数字时做加法，否则做字符串拼接；* 会把数字字符串转为数字计算。</div>
        <div class="ch-section">二、字符串操作：拼接 / 切片 / 替换</div>
        <pre>env_name + '_done'                         <span class="ch-note">// 拼接</span>
'订单号: ' + input_id                       <span class="ch-note">// 拼接常量与变量</span>
input_phone[..3]                           <span class="ch-note">// 前 3 位</span>
env_code[2..5]                             <span class="ch-note">// 第 2~5 位（不含第 5 位）</span>
env_code[2..]                              <span class="ch-note">// 从第 2 位到最后</span>
env_name.replace('a','b')                  <span class="ch-note">// 替换</span>
input_name[0]                              <span class="ch-note">// 取单个字符</span></pre>
        <div class="ch-section">三、toString 格式化：数字 / 日期</div>
        <pre>env_amount.toString('#.0##')               <span class="ch-note">// 数字格式 #.0##</span>
env_amount.toString('0.00')                 <span class="ch-note">// 保留 2 位小数</span>
env_time.toString('yyyy-MM-dd HH:mm:ss')    <span class="ch-note">// 日期格式</span>
env_time.toString('yyyy-MM-dd')             <span class="ch-note">// 只取日期部分</span></pre>
        <div class="ch-section">四、变量取值：子属性 / 数组</div>
        <pre>input_user.name                            <span class="ch-note">// 子属性</span>
input_list.length                          <span class="ch-note">// 数组长度</span>
input_list[0].name                         <span class="ch-note">// 数组第 1 个元素</span>
input_list[..3]                            <span class="ch-note">// 数组前 3 个（切片）</span></pre>
        <div class="ch-section">五、比较与逻辑（结果为 true/false）</div>
        <pre>env_score >= 60 &amp;&amp; env_score &lt; 90
(env_a + env_b) > env_c || env_flag</pre>
        <div class="ch-note" style="margin-top:8px">
          变量命名：input_ 流程入参、output_ 流程出参、env_ 中间变量、_loop_item 循环项。字符串请用引号包裹（如 'paid'）；未加引号的词若不存在同名变量也会按字符串处理。表达式结果赋值给"→ 赋值给"选择的目标变量。
        </div>
      </div>
    </el-dialog>

    <!-- 通用节点帮助弹窗（各节点完整 demo） -->
    <el-dialog v-model="nodeHelpVisible" :title="`❓ ${nodeHelpDemo?.title || ''} 使用帮助`" width="700px" append-to-body>
      <div class="condition-help">
        <template v-for="(s, i) in (nodeHelpDemo?.sections || [])" :key="i">
          <div class="ch-section">{{ s.title }}</div>
          <pre>{{ s.code }}</pre>
          <div v-if="s.note" class="ch-note" style="margin-bottom:6px">{{ s.note }}</div>
        </template>
      </div>
    </el-dialog>

    <!-- AI 智能生成流程 -->
    <el-dialog v-model="aiDialogVisible" title="🤖 AI 智能生成流程编排" width="720px" append-to-body>
      <div class="prop-item">
        <label>需求描述</label>
        <el-input v-model="aiRequirement" type="textarea" :rows="5"
          placeholder="例如：接收用户id，先调用获取用户信息接口，再根据用户id查询该用户的订单列表，最后返回用户名称和订单列表" />
      </div>
      <div class="ch-note" style="margin-bottom:8px">
        当前已加载 <b>{{ apiOptions.length }}</b> 个套件的接口，AI 会从这些接口中选择并编排（入参自动映射流程入参、出参写入 env_ 变量）。生成结果将<b>替换当前画布</b>，建议先保存当前流程。
      </div>
      <div class="prop-item">
        <label>模型</label>
        <div style="display:flex;gap:6px;align-items:center">
          <el-select v-model="aiProviderId" size="small" style="flex:1" placeholder="选择供应商" @change="onAiProviderChange">
            <el-option v-for="p in aiProviders" :key="p.id" :label="p.providerName" :value="p.id" />
          </el-select>
          <el-select v-model="aiModel" size="small" style="flex:1" placeholder="选择模型" filterable allow-create default-first-option>
            <el-option v-for="m in aiModelOptions" :key="m" :label="m" :value="m" />
          </el-select>
          <el-button size="small" @click="$router.push('/system/ai-provider')">管理</el-button>
        </div>
        <div style="font-size:11px;color:#909399;margin-top:4px">供应商与模型在 系统设置 → 大模型设置 中维护（OpenAI 兼容接口，支持 DeepSeek/通义/Kimi 等）。</div>
      </div>
      <div v-if="aiError" class="db-test-error">❌ {{ aiError }}</div>
      <div v-if="aiResult" style="margin-top:10px;color:#67c23a;font-size:13px">
        ✅ 已生成 {{ aiResult.nodes.length }} 个节点（{{ aiResultSummary }}）
      </div>
      <template #footer>
        <el-button @click="aiDialogVisible = false">取消</el-button>
        <el-button type="primary" icon="MagicStick" :loading="aiGenerating" @click="generateAiFlow">生成并替换画布</el-button>
      </template>
    </el-dialog>

    <!-- 数据库节点：SQL 编写帮助弹窗 -->
    <el-dialog v-model="dbHelpVisible" title="❓ 数据库节点 SQL 编写帮助" width="660px" append-to-body>
      <div class="condition-help">
        <div class="ch-section">一、模板变量 ${varName}</div>
        <pre>SELECT * FROM orders WHERE id = ${input_id}
UPDATE users SET status = 'paid' WHERE id = ${input_id}</pre>
        <div class="ch-note">SQL 中的 ${varName} 会在执行前替换为对应变量值。变量命名：input_ 入参、output_ 出参、env_ 中间变量、_loop_item 循环项。</div>
        <div class="ch-section">二、查询（QUERY）示例</div>
        <pre>SELECT id, name, amount, created_at
FROM orders
WHERE status = 'paid' AND amount &gt;= ${input_min}
ORDER BY created_at DESC
LIMIT 100</pre>
        <div class="ch-note">查询结果（行数组）写入"查询结果写入"选择的变量，之后可用 env_xxx[0].name、env_xxx.length 等方式读取。</div>
        <div class="ch-section">三、更改（UPDATE）示例</div>
        <pre>UPDATE orders SET status = 'shipped' WHERE id = ${input_id}
INSERT INTO logs(msg, created_at) VALUES ('${input_msg}', NOW())</pre>
        <div class="ch-note">影响行数写入"影响行数写入"选择的变量。</div>
        <div class="ch-section">四、辅助工具</div>
        <div class="ch-note">
          <b>表/视图/存储过程</b>：列出所选数据源中的对象，点击可查看字段，一键生成 SELECT / CALL 语句（自动带 100 行限制）。<br>
          <b>测试 SQL</b>：不运行流程、单独执行 SQL 验证正确性。查询返回前 100 行预览；更改操作在事务中执行并自动回滚，不影响真实数据。${varName} 可通过测试参数（JSON）提供值。
        </div>
      </div>
    </el-dialog>

    <!-- 数据库节点：表/视图/存储过程浏览 -->
    <el-dialog v-model="dbObjectDialogVisible" title="🗄 数据库对象（辅助生成 SQL）" width="1020px" append-to-body>
      <div style="display:flex;gap:10px">
        <div style="flex:1;min-width:0">
          <el-radio-group v-model="dbObjectTab" size="small" @change="onDbObjectTabChange">
            <el-radio-button value="tables">表 ({{ dbTables.length }})</el-radio-button>
            <el-radio-button value="views">视图 ({{ dbViews.length }})</el-radio-button>
            <el-radio-button value="procedures">存储过程 ({{ dbProcedures.length }})</el-radio-button>
          </el-radio-group>
          <el-input v-model="dbObjectSearch" placeholder="搜索名称" size="small" clearable style="margin:8px 0" />
          <div class="db-object-list" v-loading="dbObjectLoading">
            <div v-for="obj in filteredDbObjects" :key="obj.name" class="db-object-item"
              :class="{ active: dbSelectedObject === obj.name }" @click="selectDbObject(obj.name)">
              <span class="db-object-name">{{ obj.name }}</span>
              <el-button size="small" link type="primary" @click.stop="generateSqlFromObject(obj)">生成SQL</el-button>
            </div>
            <el-empty v-if="!dbObjectLoading && filteredDbObjects.length === 0" description="暂无对象" :image-size="40" />
          </div>
        </div>
        <div style="width:480px;flex-shrink:0">
          <div class="prop-section-title" style="margin-top:0">
            {{ dbObjectTab === 'procedures' ? '参数列表' : '字段列表' }}
            <span v-if="dbSelectedObject" style="font-size:11px;color:#909399;font-weight:normal;margin-left:6px">{{ dbSelectedObject }}</span>
            <template v-if="dbObjectTab === 'procedures'">
              <el-button size="small" icon="MagicStick" link @click="smartBindProcParams" style="margin-left:auto">智能参数绑定</el-button>
              <el-button size="small" icon="VideoPlay" type="primary" link @click="generateCallFromSelectedProc">生成调用语句</el-button>
            </template>
          </div>

          <!-- 表：字段名称/中文注释/类型/默认值/是否必填 -->
          <el-table v-if="dbObjectTab === 'tables'" :data="dbColumns" size="small" border max-height="400" v-loading="dbColumnLoading">
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
          <el-table v-else-if="dbObjectTab === 'views'" :data="dbColumns" size="small" border max-height="400" v-loading="dbColumnLoading">
            <el-table-column prop="name" label="字段名称" min-width="140" show-overflow-tooltip />
            <el-table-column prop="comment" label="中文注释" min-width="140" show-overflow-tooltip>
              <template #default="{ row }"><span style="color:#606266">{{ row.comment || '—' }}</span></template>
            </el-table-column>
            <el-table-column prop="dataType" label="类型" width="130" show-overflow-tooltip />
          </el-table>

          <!-- 存储过程：参数名/参数类型/默认值/当前值（可编辑） -->
          <el-table v-else :data="dbProcParams" size="small" border max-height="400" v-loading="dbColumnLoading">
            <el-table-column prop="name" label="参数名" min-width="110" show-overflow-tooltip />
            <el-table-column prop="dataType" label="参数类型" width="100" show-overflow-tooltip />
            <el-table-column prop="mode" label="模式" width="70" align="center">
              <template #default="{ row }"><el-tag size="small" :type="(row.mode || 'IN').toUpperCase().includes('OUT') ? 'warning' : 'primary'">{{ row.mode || 'IN' }}</el-tag></template>
            </el-table-column>
            <el-table-column prop="defaultValue" label="默认值" width="80" show-overflow-tooltip>
              <template #default="{ row }"><span style="color:#909399">{{ row.defaultValue ?? '—' }}</span></template>
            </el-table-column>
            <el-table-column label="当前值" min-width="150">
              <template #default="{ row }">
                <el-input v-model="row.currentValue" size="small" placeholder="如: input_id 或 1" :disabled="(row.mode || '').toUpperCase().includes('OUT')" />
              </template>
            </el-table-column>
          </el-table>
          <div v-if="dbObjectTab === 'procedures'" class="ch-note" style="margin-top:6px">
            当前值可填字面量（如 1 / '张三'）或变量名（如 input_id → 生成 <code>'$&#123;input_id&#125;'</code>）；留空生成 NULL。OUT/INOUT 参数不参与调用。智能参数绑定会把参数名与流程参数/变量模糊匹配后自动填充。
          </div>
          <el-empty v-if="dbObjectTab !== 'procedures' && !dbColumnLoading && dbColumns.length === 0"
            description="点击左侧表/视图查看字段" :image-size="40" />
        </div>
      </div>
    </el-dialog>

    <!-- 数据库节点：测试 SQL -->
    <el-dialog v-model="dbTestDialogVisible" title="🧪 测试 SQL" width="720px" append-to-body>
      <div class="prop-item">
        <label>测试参数（SQL 中 ${varName} 的变量值，JSON 格式）</label>
        <el-input v-model="dbTestParams" type="textarea" :rows="3" placeholder='{"input_id": 1, "input_name": "张三"}' />
      </div>
      <el-button size="small" type="primary" icon="VideoPlay" :loading="dbTestLoading" @click="runDbTest">执行测试</el-button>
      <div v-if="dbTestError" class="db-test-error">
        ❌ {{ dbTestError }}
      </div>
      <div v-if="dbTestResult" class="db-test-result">
        <div v-if="dbTestResult.operationType === 'QUERY'" style="margin-bottom:6px">
          ✅ 查询成功，共 {{ dbTestResult.rowCount }} 行{{ dbTestResult.truncated ? '（仅显示前 100 行）' : '' }}
        </div>
        <div v-else style="margin-bottom:6px">✅ 执行成功，影响 {{ dbTestResult.affectedRows }} 行（事务已回滚，未实际修改数据）</div>
        <el-table v-if="dbTestResult.operationType === 'QUERY' && dbTestResult.rows.length" :data="dbTestResult.rows" size="small" border max-height="280">
          <el-table-column v-for="c in dbTestResult.columns" :key="c.name || c" :prop="c.name || c"
            :label="c.comment ? `${c.name} (${c.comment})` : (c.name || c)" min-width="110" show-overflow-tooltip />
        </el-table>
        <div v-if="dbTestResult.operationType === 'QUERY' && dbTestResult.rows.length === 0" style="color:#909399;font-size:12px">
          （无返回行）
        </div>
      </div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'
import MonacoEditor from '../../components/MonacoEditor.vue'

// VueFlow
import { VueFlow, Position } from '@vue-flow/core'
import type { NodeMouseEvent, EdgeMouseEvent, Connection } from '@vue-flow/core'
import { Background } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import { MiniMap } from '@vue-flow/minimap'
import { Handle } from '@vue-flow/core'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import '@vue-flow/controls/dist/style.css'
import '@vue-flow/minimap/dist/style.css'

const route = useRoute()
const router = useRouter()
const flowKey = route.params.flowKey as string
const containerRef = ref<HTMLElement | null>(null)

// ====== 业务节点数据（原格式） ======
const flowInfo = ref<any>(null)
const businessNodes = ref<any[]>([])
const selectedNodeKey = ref<string | null>(null)
const selectedEdgeId = ref<string | null>(null)
const allVariables = ref<any[]>([])
const staticVariables = ref<any[]>([])
const apiOptions = ref<any[]>([])
const dataSources = ref<any[]>([])
const methodApiSelection = ref<any[]>([])
// 已发布的流程列表（供 SUB_FLOW 节点选择）
const publishedFlows = ref<any[]>([])
// METHOD 节点选中 API 的入参/出参（供下拉选择）
const selectedApiInputParams = ref<any[]>([])
const selectedApiOutputParams = ref<any[]>([])
// SUB_FLOW 节点选中子流程的入参/出参（供下拉选择）
const subFlowInputParams = ref<any[]>([])
const subFlowOutputParams = ref<any[]>([])

// ====== VueFlow 节点/边 ======
const vfNodes = ref<any[]>([])
const vfEdges = ref<any[]>([])

// ====== 撤销/重做栈 ======
interface Snapshot {
  nodes: any[]
  edges: any[]
}
const undoStack = ref<Snapshot[]>([])
const redoStack = ref<Snapshot[]>([])
let _suppressHistory = false

function takeSnapshot() {
  if (_suppressHistory) return
  undoStack.value.push({
    nodes: JSON.parse(JSON.stringify(businessNodes.value)),
    edges: JSON.parse(JSON.stringify(vfEdges.value))
  })
  // 每次操作后清空重做栈
  redoStack.value = []
  // 最多保留50步
  if (undoStack.value.length > 50) undoStack.value.shift()
}

function restoreSnapshot(snap: Snapshot) {
  _suppressHistory = true
  businessNodes.value = JSON.parse(JSON.stringify(snap.nodes))
  vfEdges.value = JSON.parse(JSON.stringify(snap.edges))
  syncBusinessNodesToVf()
  selectedNodeKey.value = null
  selectedEdgeId.value = null
  _suppressHistory = false
}

function undo() {
  if (undoStack.value.length === 0) return
  // 把当前状态压入重做栈
  redoStack.value.push({
    nodes: JSON.parse(JSON.stringify(businessNodes.value)),
    edges: JSON.parse(JSON.stringify(vfEdges.value))
  })
  const snap = undoStack.value.pop()!
  restoreSnapshot(snap)
  ElMessage.info('已撤销')
}

function redo() {
  if (redoStack.value.length === 0) return
  undoStack.value.push({
    nodes: JSON.parse(JSON.stringify(businessNodes.value)),
    edges: JSON.parse(JSON.stringify(vfEdges.value))
  })
  const snap = redoStack.value.pop()!
  restoreSnapshot(snap)
  ElMessage.info('已重做')
}

// ====== 节点颜色（minimap用） ======
function vfNodeColor(node: any) {
  const map: Record<string, string> = {
    start: '#52c41a', end: '#ff4d4f', method: '#1890ff',
    assign: '#722ed1', code: '#eb2f96', mysql: '#13c2c2',
    condition: '#fa8c16', merge: '#7c3aed', sub_flow: '#0891b2',
    loop: '#8b5cf6', delay: '#64748b', parallel: '#e11d48', notify: '#059669'
  }
  return map[node.data?.elementType?.toLowerCase()] || '#aaa'
}

// 流程参数
const paramDrawer = ref(false)
const paramTab = ref('input')
const flowInputParams = ref<any[]>([])
const flowOutputParams = ref<any[]>([])

// 流程参数—来自对象功能
const flowObjDialogVisible = ref(false)
const flowObjDialogType = ref<'input' | 'output'>('input')
const selectedFlowObjId = ref<number | null>(null)
const flowObjPreviewParams = ref<any[]>([])
const objectList = ref<any[]>([])

// 变量管理
const variableDrawer = ref(false)
const varDialogVisible = ref(false)
const varForm = ref({ variableCode: '', variableName: '', variableType: 'VARIABLE', dataType: 'string', objectCode: '', defaultValue: '' })

// ====== 调试相关 ======
const debugVisible = ref(false)
const debugLoading = ref(false)
const debugParams = ref('{}')
const debugResult = ref<any>(null)
const debugTab = ref('timeline')
// 节点调试状态：nodeKey -> 'success' | 'fail' | 'running'
const debugNodeStatus = ref<Record<string, string>>({})
// 节点调试输出：nodeKey -> log
const debugNodeOutput = ref<Record<string, any>>({})
// 节点报错信息：nodeKey -> errorMessage
const debugNodeError = ref<Record<string, string>>({})
const hasDebugHighlight = computed(() => Object.keys(debugNodeStatus.value).length > 0)

// 节点详情弹窗
const nodeDebugDetailVisible = ref(false)
const nodeDebugDetailKey = ref('')
const nodeDebugInputStr = ref('')
const nodeDebugOutputStr = ref('')
const nodeDebugDetailStr = ref('')

// 条件表达式语法帮助弹窗
const conditionHelpVisible = ref(false)
// 赋值节点表达式语法帮助弹窗
const assignHelpVisible = ref(false)

// ====== AI 智能生成流程 ======
const aiDialogVisible = ref(false)
const aiRequirement = ref('')
const aiGenerating = ref(false)
const aiError = ref('')
const aiResult = ref<any>(null)
const aiProviders = ref<any[]>([])
const aiProviderId = ref<number>(0)
const aiModel = ref('')

const aiModelOptions = computed(() => {
  const p = aiProviders.value.find((x: any) => x.id === aiProviderId.value)
  const list = String(p?.models || '').split(',').map((s: string) => s.trim()).filter(Boolean)
  if (!list.includes(p?.model)) list.unshift(p?.model || '')
  return list.filter(Boolean)
})

async function openAiDialog() {
  aiDialogVisible.value = true
  aiError.value = ''
  aiResult.value = null
  try {
    const res: any = await request.get('/ai/providers/enabled')
    aiProviders.value = res.data || []
    if (aiProviders.value.length > 0 && !aiProviderId.value) {
      aiProviderId.value = aiProviders.value[0].id
      aiModel.value = aiProviders.value[0].model || ''
    }
  } catch { /* 未配置供应商 */ }
}

function onAiProviderChange() {
  const p = aiProviders.value.find((x: any) => x.id === aiProviderId.value)
  aiModel.value = p?.model || ''
}

const aiResultSummary = computed(() => {
  const nodes = aiResult.value?.nodes || []
  return nodes.map((n: any) => nodeTypeName(n.elementType)).join(' → ')
})

// ====== AI 节点：供应商/模型选择 ======
const aiNodeModelOptions = computed(() => {
  const p = aiProviders.value.find((x: any) => x.id === selectedNode.value?.aiConfig?.providerId)
  const list = String(p?.models || '').split(',').map((s: string) => s.trim()).filter(Boolean)
  if (!list.includes(p?.model)) list.unshift(p?.model || '')
  return list.filter(Boolean)
})
function onAiNodeProviderChange() {
  const p = aiProviders.value.find((x: any) => x.id === selectedNode.value?.aiConfig?.providerId)
  if (selectedNode.value?.aiConfig && p) selectedNode.value.aiConfig.model = p.model || ''
}
function aiOutputPlaceholder(targetType: string) {
  return targetType === 'OUTPUT' ? '选择输出参数' : targetType === 'INPUT' ? '选择入参' : '选择变量'
}

/** 构建可用接口清单（供 AI 选择编排） */
function buildAiApiContext(): any[] {
  const apis: any[] = []
  for (const suite of apiOptions.value) {
    for (const child of (suite.children || [])) {
      const a = child.api || {}
      apis.push({
        suiteCode: suite.value,
        methodCode: a.methodCode || child.value,
        methodName: a.methodName || child.label,
        methodDesc: a.methodDesc || '',
        url: a.url || '',
        method: a.requestType || a.method || 'POST'
      })
    }
  }
  return apis
}

async function generateAiFlow() {
  if (!aiRequirement.value.trim()) { ElMessage.warning('请先描述编排需求'); return }
  if (!aiProviderId.value) { ElMessage.warning('请先在系统设置 → 大模型设置中启用供应商'); return }
  aiGenerating.value = true
  aiError.value = ''
  aiResult.value = null
  try {
    const res: any = await request.post('/ai/generate-flow', {
      requirement: aiRequirement.value,
      providerId: aiProviderId.value,
      model: aiModel.value || null,
      apis: buildAiApiContext(),
      inputParams: flowInputParams.value.map((p: any) => ({ code: p.paramCode, name: p.paramName })),
      outputParams: flowOutputParams.value.map((p: any) => ({ code: p.paramCode, name: p.paramName }))
    })
    const nodes = res.data?.nodes || []
    if (!nodes.length) { aiError.value = 'AI 未生成有效节点，请调整需求描述后重试'; return }
    // 归一化节点 key（确保与画布数据一致）
    const cleaned = nodes.map((n: any) => {
      const c = { ...n }
      if (!c.outgoings) c.outgoings = []
      if (!c.label) c.label = c.elementType
      return c
    })
    // 替换画布内容
    businessNodes.value = cleaned
    selectedNodeKey.value = null
    await nextTick()
    syncBusinessNodesToVf()
    aiResult.value = { nodes: cleaned }
    ElMessage.success('AI 流程已生成，请检查并保存')
  } catch (e: any) {
    aiError.value = e?.message || '生成失败，请检查模型配置'
  } finally { aiGenerating.value = false }
}

// ====== 通用节点帮助弹窗（完整 demo） ======
const nodeHelpVisible = ref(false)
const nodeHelpType = ref('')
const nodeHelpDemos: Record<string, { title: string; sections: { title: string; code: string; note?: string }[] }> = {
  START: {
    title: '开始节点',
    sections: [
      { title: '一、入参说明', code: `开始节点是流程入口，无需配置。
在右侧「流程参数 → 入参」中定义入参，流程内通过 input_参数名 读取。`, note: '例如定义了入参 userId，则流程中任何节点都可用 input_userId 取到调用方传入的值。' },
      { title: '二、后续节点使用示例', code: `// 条件节点
input_userId != null && input_userId > 0

// 数据库节点 SQL
SELECT * FROM orders WHERE user_id = ${'{input_userId}'}

// 赋值节点（表达式）
input_userId + '_done'` }
    ]
  },
  METHOD: {
    title: '方法节点（HTTP 调用）',
    sections: [
      { title: '一、入参填充（调用前：变量 → API 入参）', code: `sourceType 可选：
  常量 CONSTANT     → 固定值，如 "paid"
  变量 VARIABLE     → 流程变量 env_xxx
  入参 INPUT        → 流程入参 input_xxx
  静态 STATIC       → 全局静态变量
  子对象 SUB_PROPERTY → 变量.属性路径，如 env_user.name` },
      { title: '二、输出映射（调用后：API 响应 → 变量）', code: `source 填响应字段路径，如 data.list / code / msg
targetType 可选 变量/出参/静态/入参/子对象

示例：
  响应字段 data.userId  →  变量 env_user_id
  响应字段 code         →  出参 result_code` },
      { title: '三、Header 填充', code: `target 填 Header 名（如 Authorization）
sourceType=常量 → 填值，如 Bearer xxx
sourceType=变量 → 选择变量，运行时取变量值` },
      { title: '四、常见场景 demo', code: `// 场景：调用登录接口
入参填充: input_userName → userName
入参填充: 常量 "123456" → password
输出映射: data.token → env_token

// 场景：调用列表接口后取第一个元素
输出映射: data.list[0].name → env_first_name` }
    ]
  },
  CODE: {
    title: '代码节点（JavaScript 简化脚本）',
    sections: [
      { title: '一、读写流程变量', code: `$var.getVariableValue('key')        // 读取流程变量
$var.setVariableValue('key', value) // 写入流程变量

var name = $var.getVariableValue('input_name')
$var.setVariableValue('env_greeting', 'Hello, ' + name)` },
      { title: '二、读写静态变量（执行后自动持久化）', code: `var count = $static.getVariableValue('visit_count')
$static.setVariableValue('visit_count', count + 1)` },
      { title: '三、局部变量与运算', code: `// var/let/const 声明局部变量
var orderId = $var.getVariableValue('input_order_id')
var title = '订单号: ' + orderId + ' 已处理'
$var.setVariableValue('env_msg', title)

// 支持: 字符串拼接(+)、数字、布尔、null、局部变量引用
$var.setVariableValue('env_flag', true)
$var.setVariableValue('env_total', 100)` },
      { title: '四、完整 demo', code: `// 处理订单：拼接消息并写回
var id = $var.getVariableValue('input_order_id')
var userName = $var.getVariableValue('input_user_name')
var msg = '用户 ' + userName + ' 的订单 ' + id + ' 已创建'
$var.setVariableValue('env_result_msg', msg)
$var.setVariableValue('env_status', 'created')
$static.setVariableValue('order_count', $static.getVariableValue('order_count') + 1)` }
    ]
  },
  LOOP: {
    title: '循环节点',
    sections: [
      { title: '一、配置说明', code: `数组变量:   要遍历的数组变量名，填实际变量名（如 env_items）
当前元素:   每次迭代写入的元素变量，默认 _loop_item
当前索引:   默认 _loop_index（从 0 开始）
数组总数:   默认 _loop_total
结果收集:   默认 _loop_results（循环内节点输出自动收集）` },
      { title: '二、使用 demo', code: `// 假设有数组变量 env_items = [{name:"a"},{name:"b"}]
数组变量填: env_items

// 循环体内的节点可直接引用:
_loop_item        // 当前元素对象
_loop_item.name   // 当前元素的 name 属性
_loop_index       // 当前索引 0/1
_loop_total       // 总数 2
_loop_results     // 循环体执行结果收集数组` },
      { title: '三、典型场景', code: `// 批量调用接口：循环内放 METHOD 节点
METHOD 入参填充: _loop_item.id → id
输出映射: data → env_result

循环结束后:
env_items.length == _loop_total
_loop_results 包含每次迭代的输出` }
    ]
  },
  DELAY: {
    title: '延迟节点',
    sections: [
      { title: '一、固定时间', code: `关闭「变量动态」开关，直接填延迟毫秒数:
1000   = 1 秒
60000  = 1 分钟` },
      { title: '二、变量动态', code: `开启「变量动态」开关，填变量名（值为毫秒数）:
延迟变量填 env_delay_ms

配合赋值节点（表达式）动态计算:
env_delay_ms = env_retry_count * 1000 + 500` },
      { title: '三、典型场景', code: `// 限流: 每次请求间隔 500ms
// 等待: 等待第三方回调, 先延迟再查询
// 重试退避: 失败分支 → 延迟 → 回到调用节点` }
    ]
  },
  PARALLEL: {
    title: '并行节点',
    sections: [
      { title: '一、等待模式', code: `全部等待(ALL_WAIT): 所有分支执行完成后再继续
任一完成(ANY_FAST): 任一分支完成即继续（其余分支取消）` },
      { title: '二、使用 demo', code: `// 通过画布连线把并行节点连到多个下游节点
并行节点
├─→ METHOD: 查询用户信息
├─→ METHOD: 查询订单列表
└─→ MYSQL: 查询统计数据

三个分支同时执行，全部完成后汇聚到 MERGE 节点继续。
各分支写入不同变量避免互相覆盖。` },
      { title: '三、超时', code: `并行超时(ms): 0=不限。设置后超时未完成的分支会被取消。
场景: 多数据源并发查询 + 汇总。` }
    ]
  },
  AI: {
    title: '大模型节点',
    sections: [
      { title: '一、节点说明', code: `大模型节点调用系统设置 → 大模型设置中启用的供应商（OpenAI 兼容接口，支持 DeepSeek/通义千问/Kimi 等），适合在流程中插入 AI 能力：
- 内容生成 / 润色 / 翻译
- 文本分类 / 情感分析
- 结构化提取（要求模型返回 JSON 后用赋值节点解析）` },
      { title: '二、配置说明', code: `模型:        供应商下拉（空=第一个启用）+ 模型下拉（空=供应商默认）
输入变量:    作为用户消息发送给模型（可选流程入参/中间变量/出参）
图片输入:    多选图片变量（data URL），配置后走视觉模型识别（多模态）
系统提示词:  人设与任务说明（如"你是文案专家，输出简洁有力的文案"）
输出目标:    变量 / 出参 / 入参 + 对应参数选择（模型回复写入）` },
      { title: '三、使用 demo', code: `// 场景：流程中生成商品推荐文案
前置 ASSIGN 节点: env_product_name = "智能手表"

AI 节点:
  输入变量:    env_product_name
  系统提示词:  你是一名电商文案专家，根据商品名称生成一句吸引人的推荐语
  输出变量:    env_ad_copy

后续节点用 ${'${env_ad_copy}'} 引用生成结果（如 NOTIFY 通知 / METHOD 参数）` },
      { title: '四、注意事项', code: `1. 需先在系统设置 → 大模型设置中配置并启用供应商，否则流程执行时报错
2. 输入变量不存在时以空文本发送
3. 需要结构化结果时，在系统提示词中要求"只输出 JSON"，再用赋值节点/代码节点解析
4. 模型调用有网络延迟，建议设置节点超时（默认不限）` }
    ]
  },
  FILE_PARSE: {
    title: '文件解析节点',
    sections: [
      { title: '一、节点说明', code: `解析文件内容变量（支持 data URL、纯 base64、原始文本三种输入）：
- json → 解析为对象/数组，后续可用 env_xxx.属性 取值
- csv  → 首行为表头，解析为行字典数组
- text/xml → 保留原文
- auto → 自动尝试 JSON，失败按文本` },
      { title: '二、配置说明', code: `输入变量: 文件内容所在变量（如 env_file_content）
文件类型: auto / text / json / xml / csv
输出变量: 解析结果变量（如 env_parsed）` },
      { title: '三、使用 demo', code: `// 场景：上传的 JSON 文件解析后取字段
FILE_PARSE 节点:
  输入变量:  env_file
  文件类型:  json
  输出变量:  env_data

后续条件节点: env_data.status == 'ok'
后续方法节点入参: sourceType=变量 env_data.userId` },
      { title: '四、注意事项', code: `1. base64 输入要求纯 base64（无换行空格），data URL 优先
2. CSV 引号/逗号转义支持标准格式
3. 解析失败（如 JSON 非法）时输出原文，不中断流程` }
    ]
  },
  EXCEL_READ: {
    title: 'Excel 读取节点',
    sections: [
      { title: '一、节点说明', code: `读取 Excel 文件（.xlsx）内容变量（base64 或 data URL）：
- 默认读取第一个工作表，可指定工作表名
- 第一行为表头，后续每行转为字典对象
- 输出为行字典数组，可用 env_xxx[0].列名 取值、env_xxx.length 取行数` },
      { title: '二、使用 demo', code: `EXCEL_READ 节点:
  输入变量: env_excel_base64
  工作表名:  (留空=第一个)
  输出变量: env_rows

循环节点遍历 env_rows:
  _loop_item.姓名 / _loop_item.金额 逐行处理

条件节点: env_rows.length > 0` },
      { title: '三、注意事项', code: `1. 仅支持 .xlsx 格式（.xls 请先转换）
2. 单元格值统一按文本读取
3. 空单元格跳过，无表头时列名为 column1、column2...` }
    ]
  },
  FILE_WRITE: {
    title: '文件写入节点',
    sections: [
      { title: '一、节点说明', code: `把变量内容生成文件（data URL 格式，base64 编码）写入输出变量：
- 文本/CSV 内容原样写入
- 对象自动序列化为 JSON
- data URL 可直接下载，也可传给后续节点（如文件解析节点反向读取、通知节点附件）` },
      { title: '二、使用 demo', code: `FILE_WRITE 节点:
  内容变量: env_report_json   (对象自动转 JSON)
  文件类型: json
  输出变量: env_download_url

NOTIFY 节点消息中引用:
  下载地址: ${'${env_download_url}'}` },
      { title: '三、注意事项', code: `1. 输出为 data:xxx;base64,... 格式
2. 文件名变量可选，data URL 同时写入该变量
3. 大文件注意 base64 体积（约增加 1/3）` }
    ]
  },
  REDIS_GET: {
    title: 'Redis 缓存查询节点',
    sections: [
      { title: '一、节点说明', code: `从 Redis 按 key 取值：
- key 支持模板：user:${'${input_id}'}（${'${变量}'} 会替换为变量值）
- 值为 JSON 字符串时自动解析为对象/数组写入输出变量
- key 不存在时输出 null（条件节点可用 == null 判断缓存未命中）` },
      { title: '二、使用 demo', code: `// 场景：先查缓存，命中则跳过接口调用
REDIS_GET 节点:
  Key:      user:${'${input_user_id}'}
  输出变量: env_cached_user

条件节点（缓存命中判断）:
  分支1: env_cached_user != null  →  直接返回缓存
  默认:   调用获取用户接口 → REDIS_SET 回写缓存` },
      { title: '三、注意事项', code: `1. 需在系统设置 → Redis 配置中填写连接信息
2. 未配置 Redis 时流程执行报错并提示
3. 输出变量值可能是对象/数组/字符串/null 四种形态` }
    ]
  },
  REDIS_SET: {
    title: 'Redis 缓存设置节点',
    sections: [
      { title: '一、节点说明', code: `写入 Redis key-value：
- key 支持模板：session:${'${input_token}'}
- 值变量为对象时自动序列化为 JSON 字符串
- 可设过期秒数（0=永不过期）
- 输出变量写入 true/false 表示是否成功` },
      { title: '二、使用 demo', code: `// 场景：接口调用后回写缓存
前置 METHOD 节点: 输出映射 data → env_user（对象）

REDIS_SET 节点:
  Key:      user:${'${input_user_id}'}
  值变量:   env_user
  过期秒数: 3600
  输出变量: env_cache_ok

条件节点: env_cache_ok == false → NOTIFY 告警` },
      { title: '三、注意事项', code: `1. 需在系统设置 → Redis 配置中填写连接信息
2. 连接失败（Redis 不可用）时节点报错、流程失败
3. 值变量不存在时写入空字符串` }
    ]
  },
  TRANSFORM: {
    title: '模板转换节点',
    sections: [
      { title: '一、语法', code: `模板中 \${变量名|管道1|管道2} 占位符会被替换为变量值。
管道 | 后接转换方法:
  含 . 为静态调用   如 Math.Round、Json.Parse
  不含 . 为实例方法  如 ToFix(2)、ToUpper` },
      { title: '二、实例方法（字符串/数值）', code: `ToUpper / ToLower     大小写转换
Trim / TrimStart / TrimEnd
SubString(0,3)        截取
Replace('a','b')      替换
Split(',')            分割
ToFix(2)              保留 2 位小数
ParseInt / ParseDouble / ParseBool
ToString` },
      { title: '三、静态方法', code: `Math.Round / Math.Floor / Math.Ceil / Math.Abs
Json.Parse / Json.Stringify
Convert.ToInt / Convert.ToDouble / Convert.ToBoolean / Convert.ToString
String.Format('0.##')` },
      { title: '四、完整 demo', code: `模板:
{"user":"${'${input_name | ToUpper}'}","amount":"${'${env_amount | ToFix(2)}'}","day":"${'${env_time | SubString(0,10)}'}","total":"${'${env_count | Math.Round}'}"}

赋值给: 出参 result_json

子属性也支持:
${'${input_user.name | ToUpper}'}
${'${env_list[0].id | Math.Round}'}` }
    ]
  },
  NOTIFY: {
    title: '通知节点',
    sections: [
      { title: '一、Webhook 回调', code: `请求地址: https://your-server.com/callback
请求方法: POST / GET
自定义请求头(JSON): {"Authorization":"Bearer xxx"}
内容模板(支持 ${'${varName}'} 变量替换):
{"flowKey":"${'${flowKey}'}","status":"success","result":"${'${env_result}'}"}` },
      { title: '二、邮件通知', code: `收件人: a@xx.com,b@xx.com（逗号分隔）
邮件主题: 流程执行完成 - ${'${flowKey}'}
内容模板: 支持 ${'${varName}'} 变量替换` },
      { title: '三、失败策略', code: `「失败时中断流程」开关:
开启: 通知发送失败则流程失败
关闭: 仅记录日志，流程继续执行（默认推荐）` }
    ]
  },
  SUB_FLOW: {
    title: '子流程节点',
    sections: [
      { title: '一、入参映射（当前流程 → 子流程入参）', code: `source: 当前流程变量名（如 env_user_id / input_name）
sourceType: 变量 VARIABLE / 常量 CONSTANT
target: 子流程的入参名

示例:
  当前变量 env_user_id  →  子流程入参 userId
  常量 "paid"           →  子流程入参 status` },
      { title: '二、出参映射（子流程输出 → 当前流程变量）', code: `source: 子流程输出变量名（子流程里写的 output_xxx）
target: 当前流程变量名

示例:
  子流程输出 output_total  →  当前变量 env_sub_total` },
      { title: '三、注意事项', code: `1. 子流程需先「部署」发布，才会出现在可选列表
2. 子流程的入参名以子流程自身「流程参数」为准
3. 子流程执行日志会记录在总日志中，可展开查看` }
    ]
  }
}
const nodeHelpDemo = computed(() => nodeHelpDemos[nodeHelpType.value])
function openNodeHelp(type: string) {
  nodeHelpType.value = type
  nodeHelpVisible.value = true
}

// ====== 数据库节点辅助（表/视图/存储过程 + 测试SQL） ======
const dbHelpVisible = ref(false)
const dbObjectDialogVisible = ref(false)
const dbObjectLoading = ref(false)
const dbObjectTab = ref('tables')
const dbObjectSearch = ref('')
const dbTables = ref<any[]>([])
const dbViews = ref<any[]>([])
const dbProcedures = ref<any[]>([])
const dbSelectedObject = ref('')
const dbColumns = ref<any[]>([])
const dbColumnLoading = ref(false)
const dbProcParams = ref<any[]>([])
const dbTestDialogVisible = ref(false)
const dbTestParams = ref('{}')
const dbTestLoading = ref(false)
const dbTestResult = ref<any>(null)
const dbTestError = ref('')

const filteredDbObjects = computed(() => {
  const list = dbObjectTab.value === 'tables' ? dbTables.value : dbObjectTab.value === 'views' ? dbViews.value : dbProcedures.value
  const kw = dbObjectSearch.value.trim().toLowerCase()
  return kw ? list.filter((o: any) => (o.name || '').toLowerCase().includes(kw)) : list
})

/** 当前节点所选数据源的类型（用于生成方言相关的 SQL） */
function selectedDsType(): string {
  const name = selectedNode.value?.mysqlConfig?.dataSourceName
  return (dataSources.value.find((d: any) => d.dataSourceName === name)?.dataSourceType || 'mysql').toLowerCase()
}

async function openDbObjectDialog() {
  const name = selectedNode.value?.mysqlConfig?.dataSourceName
  if (!name) { ElMessage.warning('请先选择数据源'); return }
  dbObjectDialogVisible.value = true
  dbObjectLoading.value = true
  dbTables.value = []; dbViews.value = []; dbProcedures.value = []
  dbColumns.value = []; dbProcParams.value = []
  dbSelectedObject.value = ''; dbObjectSearch.value = ''
  try {
    const res: any = await request.post('/system/datasource/metadata', { dataSourceName: name })
    dbTables.value = res.data?.tables || []
    dbViews.value = res.data?.views || []
    dbProcedures.value = res.data?.procedures || []
  } catch { /* 拦截器已提示 */ } finally { dbObjectLoading.value = false }
}

function onDbObjectTabChange() {
  dbSelectedObject.value = ''
  dbColumns.value = []
  dbProcParams.value = []
}

async function selectDbObject(name: string) {
  dbSelectedObject.value = name
  if (dbObjectTab.value === 'procedures') { await loadDbProcParams(name); return }
  await loadDbColumns(name)
}

async function loadDbColumns(name: string) {
  dbColumnLoading.value = true
  dbColumns.value = []
  try {
    const res: any = await request.post('/system/datasource/columns', {
      dataSourceName: selectedNode.value.mysqlConfig.dataSourceName, tableName: name
    })
    dbColumns.value = res.data || []
  } catch { dbColumns.value = [] } finally { dbColumnLoading.value = false }
}

async function loadDbProcParams(name: string) {
  dbColumnLoading.value = true
  dbProcParams.value = []
  try {
    const res: any = await request.post('/system/datasource/procedure-params', {
      dataSourceName: selectedNode.value.mysqlConfig.dataSourceName, procName: name
    })
    dbProcParams.value = (res.data || []).map((p: any) => ({ ...p, currentValue: p.defaultValue || '' }))
  } catch { dbProcParams.value = [] } finally { dbColumnLoading.value = false }
}

/** 智能参数绑定：参数名与流程入参/出参/变量模糊匹配，自动填充当前值 */
function smartBindProcParams() {
  const candidates: { code: string; name: string }[] = []
  flowInputParams.value.forEach((p: any) => candidates.push({ code: `input_${p.paramCode}`, name: p.paramName || p.paramCode }))
  flowOutputParams.value.forEach((p: any) => candidates.push({ code: `output_${p.paramCode}`, name: p.paramName || p.paramCode }))
  allVariables.value.forEach((v: any) => candidates.push({ code: v.variableCode, name: v.variableName || v.variableCode }))
  const norm = (s: string) => (s || '').toLowerCase().replace(/[_\s]/g, '')
  let bound = 0
  dbProcParams.value.forEach((param: any) => {
    if ((param.mode || '').toUpperCase().includes('OUT')) return
    const pn = norm(param.name)
    let best: { code: string; score: number } | null = null
    for (const c of candidates) {
      const cn = norm(c.code); const nn = norm(c.name)
      let score = 0
      if (pn === cn || pn === nn) score = 3
      else if (pn && (cn.includes(pn) || pn.includes(cn) || nn.includes(pn) || pn.includes(nn))) score = 2
      if (score > 0 && (!best || score > best.score)) best = { code: c.code, score }
    }
    if (best) { param.currentValue = best.code; bound++ }
  })
  ElMessage.success(`已智能绑定 ${bound} 个参数（当前值）`)
}

/** 生成存储过程调用语句（按参数当前值） */
function generateCallFromSelectedProc() {
  const proc = dbProcedures.value.find((p: any) => p.name === dbSelectedObject.value)
  if (!proc) { ElMessage.warning('请先在左侧选择存储过程'); return }
  generateSqlFromObject(proc)
}

async function generateSqlFromObject(obj: any) {
  const node = selectedNode.value
  if (!node?.mysqlConfig) return
  dbSelectedObject.value = obj.name
  const dsType = selectedDsType()
  let sql = ''
  if (dbObjectTab.value === 'procedures') {
    // 确保参数列表已加载
    if (!dbProcParams.value.length) await loadDbProcParams(obj.name)
    const args = dbProcParams.value
      .filter((p: any) => !(p.mode || '').toUpperCase().includes('OUT'))
      .map((p: any) => formatCallArg(p.currentValue))
    sql = dsType === 'sqlserver' || dsType === 'mssql' ? `EXEC ${obj.name} ${args.join(', ')}`
      : dsType === 'oracle' || dsType === 'dm' ? `BEGIN ${obj.name}(${args.join(', ')}); END;`
      : `CALL ${obj.name}(${args.join(', ')})`
  } else {
    // 表/视图先取字段（保证生成含字段列表的 SELECT）
    await loadDbColumns(obj.name)
    const cols = dbColumns.value.length ? dbColumns.value.map((c: any) => c.name).join(', ') : '*'
    const from = `FROM ${obj.name}`
    if (dsType === 'sqlserver' || dsType === 'mssql') sql = `SELECT TOP 100 ${cols}\n${from}`
    else if (dsType === 'oracle' || dsType === 'dm') sql = `SELECT ${cols}\n${from}\nWHERE ROWNUM <= 100`
    else sql = `SELECT ${cols}\n${from}\nLIMIT 100`
  }
  node.mysqlConfig.sql = sql
  dbObjectDialogVisible.value = false
  ElMessage.success('已生成 SQL')
}

/** 存储过程参数 → 调用实参文本：字面量/数字原样，变量名转为 '${var}'，空 → NULL */
function formatCallArg(v: string): string {
  const val = (v || '').trim()
  if (!val) return 'NULL'
  if (/^-?\d+(\.\d+)?$/.test(val) || val.toUpperCase() === 'NULL') return val
  if (/^[A-Za-z_][A-Za-z0-9_]*$/.test(val)) return "'\${" + val + "}'"
  return "'" + val.replace(/'/g, "''") + "'"
}

function openDbTestDialog() {
  const node = selectedNode.value
  if (!node?.mysqlConfig?.dataSourceName) { ElMessage.warning('请先选择数据源'); return }
  dbTestDialogVisible.value = true
  dbTestResult.value = null
  dbTestError.value = ''
  // 自动提取 SQL 中的 ${varName} 生成参数模板
  const sql = node.mysqlConfig.sql || ''
  const names: string[] = []
  for (const m of sql.matchAll(/\$\{([^}]+)\}/g)) {
    const v = m[1].trim()
    if (v && !names.includes(v)) names.push(v)
  }
  const params: Record<string, string> = {}
  names.forEach(n => { params[n] = '' })
  dbTestParams.value = names.length ? JSON.stringify(params, null, 2) : '{}'
}

async function runDbTest() {
  const node = selectedNode.value
  const sql = node?.mysqlConfig?.sql
  if (!sql?.trim()) { ElMessage.warning('请先编写 SQL'); return }
  let params: Record<string, any> = {}
  try { params = JSON.parse(dbTestParams.value || '{}') } catch {
    dbTestError.value = '测试参数不是合法的 JSON'
    dbTestResult.value = null
    return
  }
  dbTestLoading.value = true
  dbTestError.value = ''
  dbTestResult.value = null
  try {
    const res: any = await request.post('/system/datasource/test-sql', {
      dataSourceName: node.mysqlConfig.dataSourceName,
      sql,
      operationType: node.mysqlConfig.operationType || 'QUERY',
      params
    })
    dbTestResult.value = res.data
  } catch (e: any) {
    dbTestError.value = e?.message || '测试失败'
  } finally { dbTestLoading.value = false }
}

const debugResultStr = computed(() => debugResult.value ? JSON.stringify(debugResult.value, null, 2) : '')
const debugOutputStr = computed(() => {
  if (!debugResult.value) return ''
  // 后端返回字段名为 outputs（带s），兼容多种字段名
  const out = debugResult.value.outputs ?? debugResult.value.output ?? debugResult.value.outputVariables ?? debugResult.value.result
  return out ? JSON.stringify(out, null, 2) : JSON.stringify(debugResult.value, null, 2)
})

function getLogSeq(log: any, idx: any): number {
  const n = parseInt(log.sortNum)
  return isNaN(n) ? (Number(idx) + 1) : n
}

function showNodeDebugDetail(nodeKey: string) {
  const log = debugNodeOutput.value[nodeKey]
  if (!log) return
  nodeDebugDetailKey.value = nodeKey
  nodeDebugInputStr.value = log.inputSnapshot ? JSON.stringify(log.inputSnapshot, null, 2) : (log.inputVariables ? JSON.stringify(log.inputVariables, null, 2) : '{}')
  nodeDebugOutputStr.value = log.outputSnapshot ? JSON.stringify(log.outputSnapshot, null, 2) : (log.outputVariables ? JSON.stringify(log.outputVariables, null, 2) : '{}')
  nodeDebugDetailStr.value = JSON.stringify(log, null, 2)
  nodeDebugDetailVisible.value = true
}

function clearDebugHighlight() {
  debugNodeStatus.value = {}
  debugNodeOutput.value = {}
  debugNodeError.value = {}
  // 刷新节点以清除高亮
  vfNodes.value = vfNodes.value.map(n => ({ ...n }))
}

const dataTypes = [
  { value: 'string',  label: 'string' },
  { value: 'integer', label: 'integer' },
  { value: 'double',  label: 'double' },
  { value: 'boolean', label: 'boolean' },
  { value: 'date',    label: 'date' },
  { value: 'date',    label: 'date（日期）' },
  { value: 'json',    label: 'json（JSON对象）' },
  { value: 'object',  label: 'object（对象类型）' },
  { value: 'array',   label: 'array（对象数组）' },
]

// 检测是否有参数使用了 object/array 类型
const hasObjectParams = (type: 'input' | 'output') => {
  const params = type === 'input' ? flowInputParams.value : flowOutputParams.value
  return params.some(p => p.dataType === 'object' || p.dataType === 'array')
}
const hasObjectVars = computed(() => allVariables.value.some(v => v.dataType === 'object' || v.dataType === 'array'))

// 类型切换时清除 objectCode（如果不再需要）
function onFlowParamTypeChange(row: any) {
  if (row.dataType !== 'object' && row.dataType !== 'array') row.objectCode = ''
}
function onFlowParamObjChange(row: any, objectCode: string) {
  row.objectCode = objectCode
}
function onVarTypeChange(row: any) {
  if (row.dataType !== 'object' && row.dataType !== 'array') row.objectCode = ''
}

// 所有非简单类型（object/array）的参数/变量，供"子对象"选择
const allComplexTypes = computed(() => {
  const items: any[] = []
  for (const p of flowInputParams.value) {
    if (p.dataType === 'object' || p.dataType === 'array') items.push({ code: p.paramCode, name: p.paramName, type: 'input', objectCode: p.objectCode, dataType: p.dataType })
  }
  for (const p of flowOutputParams.value) {
    if (p.dataType === 'object' || p.dataType === 'array') items.push({ code: p.paramCode, name: p.paramName, type: 'output', objectCode: p.objectCode, dataType: p.dataType })
  }
  for (const v of allVariables.value) {
    if (v.dataType === 'object' || v.dataType === 'array') items.push({ code: v.variableCode, name: v.variableName, type: 'variable', objectCode: v.objectCode, dataType: v.dataType })
  }
  for (const s of staticVariables.value) {
    if (s.dataType === 'object' || s.dataType === 'array') items.push({ code: s.varCode, name: s.varName, type: 'static', objectCode: s.objectCode, dataType: s.dataType })
  }
  return items
})

// 所有数组类型的参数/变量，供"数组操作"选择
const allArrayTypes = computed(() => {
  const items: any[] = []
  for (const p of flowInputParams.value) {
    if (p.dataType === 'array') items.push({ code: p.paramCode, name: p.paramName, type: 'input', objectCode: p.objectCode })
  }
  for (const p of flowOutputParams.value) {
    if (p.dataType === 'array') items.push({ code: p.paramCode, name: p.paramName, type: 'output', objectCode: p.objectCode })
  }
  for (const v of allVariables.value) {
    if (v.dataType === 'array') items.push({ code: v.variableCode, name: v.variableName, type: 'variable', objectCode: v.objectCode })
  }
  for (const s of staticVariables.value) {
    if (s.dataType === 'array') items.push({ code: s.varCode, name: s.varName, type: 'static', objectCode: s.objectCode })
  }
  return items
})

const selectedNode = computed(() => businessNodes.value.find(n => n.key === selectedNodeKey.value) || null)
const selectedEdgeInfo = computed(() => selectedEdgeId.value ? vfEdges.value.find(e => e.id === selectedEdgeId.value) : null)
const hasStart = computed(() => businessNodes.value.some(n => n.elementType === 'START'))
const hasEnd = computed(() => businessNodes.value.some(n => n.elementType === 'END'))
const otherNodes = computed(() => businessNodes.value.filter(n => n.key !== selectedNodeKey.value))

watch(selectedNode, (node) => {
  if (node?.elementType === 'METHOD' && node.method?.suiteCode && node.method?.methodCode) {
    methodApiSelection.value = [node.method.suiteCode, node.method.methodCode]
    // 加载 API 入参/出参
    const suiteOption = apiOptions.value.find(s => s.value === node.method.suiteCode)
    const apiOption = suiteOption?.children?.find((a: any) => a.value === node.method.methodCode)
    if (apiOption?.api?.id) loadApiParams(apiOption.api.id)
  } else {
    methodApiSelection.value = []
    selectedApiInputParams.value = []
    selectedApiOutputParams.value = []
  }
  // SUB_FLOW 节点：选中时加载子流程的入参/出参
  if (node?.elementType === 'SUB_FLOW' && node.subFlowConfig?.subFlowKey) {
    loadSubFlowParams(node.subFlowConfig.subFlowKey)
  } else if (node?.elementType !== 'SUB_FLOW') {
    subFlowInputParams.value = []
    subFlowOutputParams.value = []
  }
})

// 监听子流程 key 变化，自动加载入参/出参
watch(() => selectedNode.value?.subFlowConfig?.subFlowKey, (newKey) => {
  if (newKey) loadSubFlowParams(newKey)
})

async function loadSubFlowParams(flowKey: string) {
  try {
    // 查询子流程的最新版本对应的流程定义入参/出参
    const defRes: any = await request.get(`/flow/definition/infoByKey/${flowKey}`)
    subFlowInputParams.value = defRes.data?.inputParams || []
    subFlowOutputParams.value = defRes.data?.outputParams || []
  } catch {
    subFlowInputParams.value = []
    subFlowOutputParams.value = []
  }
}

// ====== 键盘事件 ======
function onDeleteKey(e: KeyboardEvent) {
  // 如果焦点在输入框/文本域里，不触发删除
  const tag = (e.target as HTMLElement)?.tagName?.toLowerCase()
  if (tag === 'input' || tag === 'textarea') return

  if (selectedEdgeId.value) {
    removeEdge(selectedEdgeId.value)
  } else if (selectedNodeKey.value) {
    removeNode(selectedNodeKey.value)
  }
}

// 点击画布空白处取消选中
function onPaneClick() {
  selectedNodeKey.value = null
  selectedEdgeId.value = null
}

// 让容器获取焦点（以便接收键盘事件）
onMounted(async () => {
  await Promise.all([loadFlowInfo(), loadSuiteApis(), loadDataSources(), loadStaticVariables(), loadPublishedFlows(), loadObjects()])
  nextTick(() => { containerRef.value?.focus() })
})

// ====== VueFlow 事件 ======
function onVfNodeClick(evt: NodeMouseEvent) {
  selectedEdgeId.value = null
  selectNodeByKey(evt.node.data.nodeKey)
  // 聚焦容器以接收键盘事件
  nextTick(() => { containerRef.value?.focus() })
}

function onVfEdgeClick(evt: EdgeMouseEvent) {
  selectedNodeKey.value = null
  selectedEdgeId.value = evt.edge.id
  nextTick(() => { containerRef.value?.focus() })
}

function onVfConnect(params: Connection) {
  takeSnapshot()
  const srcKey = vfNodes.value.find((n: any) => n.id === params.source)?.data?.nodeKey
  const tgtKey = vfNodes.value.find((n: any) => n.id === params.target)?.data?.nodeKey
  if (!srcKey || !tgtKey) return

  const srcNode = businessNodes.value.find(n => n.key === srcKey)
  const tgtNode = businessNodes.value.find(n => n.key === tgtKey)
  if (!srcNode || !tgtNode) return

  if (!srcNode.outgoings) srcNode.outgoings = []
  if (!tgtNode.incomings) tgtNode.incomings = []
  if (!srcNode.outgoings.includes(tgtKey)) srcNode.outgoings.push(tgtKey)
  if (!tgtNode.incomings.includes(srcKey)) tgtNode.incomings.push(srcKey)

  if (srcNode.elementType === 'CONDITION' && srcNode.conditions) {
    const emptyCond = srcNode.conditions.find((c: any) => !c.outgoing)
    if (emptyCond) emptyCond.outgoing = tgtKey
  }

  const edgeId = `e-${params.source}-${params.target}`
  if (!vfEdges.value.find(e => e.id === edgeId)) {
    vfEdges.value.push({
      id: edgeId,
      source: params.source,
      target: params.target,
      animated: true,
      style: { stroke: '#1890ff', strokeWidth: 2 },
      markerEnd: { type: 'arrowclosed', color: '#1890ff' }
    })
  }
}

function onVfEdgeUpdate(_evt: { edge: any; connection: any }) {}

function selectNodeByKey(key: string) {
  selectedNodeKey.value = key
  selectedEdgeId.value = null
}

// ====== 业务节点 → VueFlow 节点转换 ======
function buildVfNode(bNode: any, x: number, y: number) {
  return {
    id: bNode.key,
    type: 'juggle',
    position: { x: bNode._x ?? x, y: bNode._y ?? y },
    data: {
      nodeKey: bNode.key,
      elementType: bNode.elementType,
      label: bNode.label || ''
    },
    draggable: true
  }
}

function buildVfEdge(srcKey: string, tgtKey: string) {
  return {
    id: `e-${srcKey}-${tgtKey}`,
    source: srcKey,
    target: tgtKey,
    animated: true,
    style: { stroke: '#1890ff', strokeWidth: 2 },
    markerEnd: { type: 'arrowclosed', color: '#1890ff' }
  }
}

function syncBusinessNodesToVf() {
  const cols = 1
  const xBase = 100
  const yBase = 60
  const xGap = 200
  const yGap = 120

  const newVfNodes: any[] = []
  const newVfEdges: any[] = []
  const edgeSet = new Set<string>()

  businessNodes.value.forEach((bNode, idx) => {
    const x = bNode._x ?? xBase + (idx % cols) * xGap
    const y = bNode._y ?? yBase + idx * yGap
    newVfNodes.push(buildVfNode(bNode, x, y))
  })

  businessNodes.value.forEach(bNode => {
    const outs: string[] = bNode.outgoings || []
    outs.forEach((tgt: string) => {
      const eid = `e-${bNode.key}-${tgt}`
      if (!edgeSet.has(eid)) {
        edgeSet.add(eid)
        newVfEdges.push(buildVfEdge(bNode.key, tgt))
      }
    })
    if (bNode.elementType === 'CONDITION' && bNode.conditions) {
      bNode.conditions.forEach((c: any) => {
        if (c.outgoing) {
          const eid = `e-${bNode.key}-${c.outgoing}`
          if (!edgeSet.has(eid)) {
            edgeSet.add(eid)
            newVfEdges.push({
              ...buildVfEdge(bNode.key, c.outgoing),
              label: c.conditionName || '',
              style: { stroke: '#fa8c16', strokeWidth: 2, strokeDasharray: '5,3' },
              labelStyle: { fill: '#fa8c16', fontWeight: 600, fontSize: 11 },
              markerEnd: { type: 'arrowclosed', color: '#fa8c16' }
            })
          }
        }
      })
    }
  })

  vfNodes.value = newVfNodes
  vfEdges.value = newVfEdges
}

function syncVfPositionsToBusinessNodes() {
  vfNodes.value.forEach(vfn => {
    const bNode = businessNodes.value.find(n => n.key === vfn.id)
    if (bNode) {
      bNode._x = vfn.position.x
      bNode._y = vfn.position.y
    }
  })
}

function syncVfNodeLabel(bNode: any) {
  const vfn = vfNodes.value.find(n => n.id === bNode.key)
  if (vfn) vfn.data = { ...vfn.data, label: bNode.label }
}

// ====== 自动布局 ======
function autoLayout() {
  if (businessNodes.value.length === 0) return

  const NODE_W = 160   // 节点宽度（含间距）
  const NODE_H = 140   // 节点高度（含间距）

  const nodeMap = new Map<string, any>()
  businessNodes.value.forEach(n => nodeMap.set(n.key, n))

  // 收集所有边（包含 CONDITION 的 conditions 分支）
  function getOutgoings(node: any): string[] {
    const outs: string[] = []
    if (node.outgoings) outs.push(...node.outgoings)
    if (node.elementType === 'CONDITION' && node.conditions) {
      node.conditions.forEach((c: any) => {
        if (c.outgoing && !outs.includes(c.outgoing)) outs.push(c.outgoing)
      })
    }
    return outs.filter(k => nodeMap.has(k))
  }

  // 计算每个节点的入度
  const inDegree = new Map<string, number>()
  businessNodes.value.forEach(n => inDegree.set(n.key, 0))
  businessNodes.value.forEach(n => {
    getOutgoings(n).forEach(k => inDegree.set(k, (inDegree.get(k) || 0) + 1))
  })

  // 找到起点（START 优先，否则入度0）
  const startNode = businessNodes.value.find(n => n.elementType === 'START')
    || businessNodes.value.find(n => (inDegree.get(n.key) || 0) === 0)
  if (!startNode) {
    // fallback：简单竖排
    businessNodes.value.forEach((n, i) => { n._x = 200; n._y = 60 + i * NODE_H })
    syncBusinessNodesToVf()
    ElMessage.success('已自动布局')
    return
  }

  // 递归布局：返回「该子树」实际占用的总宽度（列数 * NODE_W），并写入 _x/_y
  // colOffset: 当前子树的左侧列偏移（列数）
  // row: 当前起始行（层级）
  // visited: 防止死循环
  // 返回：[占用的列宽（列数）, 最深行号]
  const positioned = new Map<string, boolean>()

  function layoutSubtree(nodeKey: string, colOffset: number, row: number): [number, number] {
    if (!nodeMap.has(nodeKey)) return [1, row]
    const node = nodeMap.get(nodeKey)!

    // 如果已经定位过（如 MERGE 节点被多个分支共享），直接跳过
    if (positioned.get(nodeKey)) return [1, row]
    positioned.set(nodeKey, true)

    const outs = getOutgoings(node)

    if (node.elementType === 'CONDITION' && node.conditions && node.conditions.length > 0) {
      // -------- CONDITION 节点：先放置自身，然后横向展开各分支 --------
      node._x = colOffset * NODE_W + 100
      node._y = row * NODE_H + 60

      // 收集有效分支
      const branches: string[] = []
      node.conditions.forEach((c: any) => { if (c.outgoing && nodeMap.has(c.outgoing)) branches.push(c.outgoing) })
      // outgoings 里可能还有 MERGE 节点直连的情况，也加入（已去重）
      node.outgoings?.forEach((k: string) => { if (nodeMap.has(k) && !branches.includes(k)) branches.push(k) })

      if (branches.length === 0) return [1, row + 1]

      // 布局各分支，横向并排
      let totalCols = 0
      let maxRow = row + 1
      const branchResults: Array<[string, number, number, number]> = [] // [key, colStart, cols, maxRow]

      branches.forEach(branchKey => {
        const branchNode = nodeMap.get(branchKey)!
        // 跳过 MERGE 节点（汇聚点）
        if (branchNode?.elementType === 'MERGE') {
          branchResults.push([branchKey, colOffset + totalCols, 0, row + 1])
          return
        }
        const [cols, deepRow] = layoutSubtree(branchKey, colOffset + totalCols, row + 1)
        branchResults.push([branchKey, colOffset + totalCols, cols, deepRow])
        totalCols += cols
        if (deepRow > maxRow) maxRow = deepRow
      })

      const spanCols = Math.max(totalCols, 1)
      // 让 CONDITION 节点水平居中于所有分支
      node._x = (colOffset + spanCols / 2 - 0.5) * NODE_W + 100

      // 找汇聚的 MERGE 节点
      const mergeKey = findMergeForCondition(node, nodeMap)
      let afterRow = maxRow + 1
      if (mergeKey && nodeMap.has(mergeKey) && !positioned.get(mergeKey)) {
        const mergeNode = nodeMap.get(mergeKey)!
        mergeNode._x = (colOffset + spanCols / 2 - 0.5) * NODE_W + 100
        mergeNode._y = afterRow * NODE_H + 60
        positioned.set(mergeKey, true)

        // MERGE 之后的节点继续主干布局
        const mergeOuts = (mergeNode.outgoings || []).filter((k: string) => nodeMap.has(k))
        let curRow = afterRow + 1
        for (const nextKey of mergeOuts) {
          const [, deepRow] = layoutSubtree(nextKey, colOffset + Math.floor(spanCols / 2), curRow)
          curRow = deepRow + 1
        }
        return [spanCols, curRow]
      }

      // 没有 MERGE，继续顺序布局 outgoings 里不在 branches 里的节点
      const mainContinue = (node.outgoings || []).filter((k: string) => nodeMap.has(k) && !branches.includes(k))
      let curRow = afterRow
      for (const nextKey of mainContinue) {
        const [, deepRow] = layoutSubtree(nextKey, colOffset + Math.floor(spanCols / 2), curRow)
        curRow = deepRow + 1
      }
      return [spanCols, curRow]

    } else {
      // -------- 普通节点（含 START/END/METHOD/ASSIGN/CODE/MYSQL/MERGE） --------
      node._x = colOffset * NODE_W + 100
      node._y = row * NODE_H + 60

      if (outs.length === 0) return [1, row]

      let curRow = row + 1
      for (const nextKey of outs) {
        const [, deepRow] = layoutSubtree(nextKey, colOffset, curRow)
        curRow = deepRow + 1
      }
      return [1, curRow - 1]
    }
  }

  // 找 CONDITION 节点分支的汇聚 MERGE（BFS）
  function findMergeForCondition(condNode: any, nm: Map<string, any>): string | null {
    const branches: string[] = []
    condNode.conditions?.forEach((c: any) => { if (c.outgoing) branches.push(c.outgoing) })
    if (branches.length === 0) return null
    for (const b of branches) {
      const found = bfsFindMerge(b, nm, new Set<string>())
      if (found) return found
    }
    return null
  }

  function bfsFindMerge(key: string, nm: Map<string, any>, visited: Set<string>): string | null {
    if (!key || visited.has(key) || !nm.has(key)) return null
    visited.add(key)
    const n = nm.get(key)!
    if (n.elementType === 'MERGE') return key
    for (const k of (n.outgoings || [])) {
      const r = bfsFindMerge(k, nm, visited)
      if (r) return r
    }
    if (n.conditions) {
      for (const c of n.conditions) {
        if (c.outgoing) {
          const r = bfsFindMerge(c.outgoing, nm, visited)
          if (r) return r
        }
      }
    }
    return null
  }

  // 开始布局
  layoutSubtree(startNode.key, 0, 0)

  // 处理未被访问的孤立节点
  let orphanRow = 0
  businessNodes.value.forEach(n => {
    if (!positioned.get(n.key)) {
      n._x = 100 + 2 * NODE_W
      n._y = 60 + orphanRow * NODE_H
      orphanRow++
    }
  })

  syncBusinessNodesToVf()
  ElMessage.success('已自动布局')
}

// ====== 数据加载 ======
async function loadFlowInfo() {
  try {
    const res: any = await request.get(`/flow/definition/infoByKey/${flowKey}`)
    const def = res.data?.definition || res.data
    flowInfo.value = def
    if (def?.flowContent && def.flowContent !== '[]') {
      try {
        businessNodes.value = JSON.parse(def.flowContent)
      } catch { businessNodes.value = [] }
    }
    allVariables.value = res.data?.variables || []
    flowInputParams.value = res.data?.inputParams || []
    flowOutputParams.value = res.data?.outputParams || []
    if (flowInputParams.value.length > 0) {
      const defaultObj: Record<string, any> = {}
      for (const p of flowInputParams.value) {
        defaultObj[p.paramCode] = p.defaultValue || ''
      }
      debugParams.value = JSON.stringify(defaultObj, null, 2)
    }
    await nextTick()
    syncBusinessNodesToVf()
  } catch (e) {
    console.error('loadFlowInfo', e)
  }
}

async function loadSuiteApis() {
  try {
    const suitesRes: any = await request.get('/suite/list')
    const suites = suitesRes.data || []
    const options: any[] = []
    for (const suite of suites) {
      const apisRes: any = await request.post('/suite/api/list', { suiteCode: suite.suiteCode })
      const apis = apisRes.data || []
      if (apis.length > 0) {
        options.push({
          value: suite.suiteCode,
          label: suite.suiteName,
          children: apis.map((a: any) => ({
            value: a.methodCode,
            label: a.methodName,
            api: a
          }))
        })
      }
    }
    apiOptions.value = options
  } catch {}
}

async function loadDataSources() {
  try {
    const res: any = await request.get('/system/datasource/list')
    dataSources.value = res.data || []
  } catch {}
}

async function loadStaticVariables() {
  try {
    const res: any = await request.get('/system/static-var/list')
    staticVariables.value = res.data || []
  } catch {}
}

async function loadObjects() {
  try {
    const res: any = await request.get('/object/list')
    objectList.value = res.data || []
  } catch {}
}

async function loadPublishedFlows() {
  try {
    // 从 FlowDefinition 表加载所有流程（无需部署），排除自身 flowKey
    // 原来从 FlowInfo 加载会导致未部署的流程无法选择（子流程下拉为空）
    const res: any = await request.post('/flow/definition/page', { pageNum: 1, pageSize: 200 })
    publishedFlows.value = (res.data?.records || []).filter((f: any) => f.flowKey !== flowKey)
  } catch {}
}

// ====== 节点操作 ======
function nodeIcon(type: string) {
  const map: Record<string, string> = {
    START: '▶', END: '⏹', METHOD: '⚙', CONDITION: '◆',
    ASSIGN: '←', CODE: '{ }', MYSQL: '⊕', MERGE: '⇒', SUB_FLOW: '⬡',
    LOOP: '↻', DELAY: '⏱', PARALLEL: '∥', NOTIFY: '✉', TRANSFORM: '📝', AI: '🤖',
    FILE_PARSE: '📄', EXCEL_READ: '📊', FILE_WRITE: '💾', REDIS_GET: '🔎', REDIS_SET: '🔏'
  }
  return map[type] || '?'
}

function nodeTypeName(type: string) {
  const map: Record<string, string> = {
    START: '开始', END: '结束', METHOD: '方法', CONDITION: '条件',
    ASSIGN: '赋值', CODE: '代码', MYSQL: '数据库', MERGE: '聚合', SUB_FLOW: '子流程',
    LOOP: '循环', DELAY: '延迟', PARALLEL: '并行', NOTIFY: '通知', TRANSFORM: '模板转换', AI: '大模型',
    FILE_PARSE: '文件解析', EXCEL_READ: 'Excel读取', FILE_WRITE: '文件写入',
    REDIS_GET: 'Redis查询', REDIS_SET: 'Redis设置'
  }
  return map[type] || type
}

// 左侧节点工具列表
const nodeToolList = [
  { type: 'START', icon: '▶', label: '开始' },
  { type: 'END', icon: '⏹', label: '结束' },
  { type: 'METHOD', icon: '⚙', label: '方法' },
  { type: 'SUB_FLOW', icon: '⬡', label: '子流程' },
  { type: 'ASSIGN', icon: '←', label: '赋值' },
  { type: 'CODE', icon: '{ }', label: '代码' },
  { type: 'MYSQL', icon: '⊕', label: '数据库' },
  { type: 'CONDITION', icon: '◆', label: '条件' },
  { type: 'MERGE', icon: '⇒', label: '聚合' },
  { type: 'LOOP', icon: '↻', label: '循环' },
  { type: 'DELAY', icon: '⏱', label: '延迟' },
  { type: 'PARALLEL', icon: '∥', label: '并行' },
  { type: 'NOTIFY', icon: '✉', label: '通知' },
  { type: 'TRANSFORM', icon: '📝', label: '模板转换' },
  { type: 'AI', icon: '🤖', label: '大模型' },
  { type: 'FILE_PARSE', icon: '📄', label: '文件解析' },
  { type: 'EXCEL_READ', icon: '📊', label: 'Excel读取' },
  { type: 'FILE_WRITE', icon: '💾', label: '文件写入' },
  { type: 'REDIS_GET', icon: '🔎', label: 'Redis查询' },
  { type: 'REDIS_SET', icon: '🔏', label: 'Redis设置' },
]

function addNode(type: string) {
  takeSnapshot()
  const key = `${type.toLowerCase()}_${Date.now()}`
  const lastVfNode = vfNodes.value[vfNodes.value.length - 1]
  const x = lastVfNode ? lastVfNode.position.x + 220 : 200
  const y = lastVfNode ? lastVfNode.position.y : 200

  const bNode: any = { key, elementType: type, incomings: [], outgoings: [], label: '', _x: x, _y: y, timeout: 0, retryCount: 0, retryInterval: 1000 }
  if (type === 'METHOD') {
    bNode.method = {
      suiteCode: '', methodCode: '', url: '', requestType: 'GET', contentType: 'JSON',
      inputFillRules: [], outputFillRules: [], headerFillRules: []
    }
  }
  if (type === 'CONDITION') {
    bNode.conditions = [
      { conditionName: '分支1', conditionType: 'CUSTOM', expression: '', outgoing: '' },
      { conditionName: '默认', conditionType: 'DEFAULT', expression: '', outgoing: '' }
    ]
  }
  if (type === 'ASSIGN') bNode.assignRules = []
  if (type === 'CODE') bNode.codeConfig = { scriptType: 'javascript', script: '' }
  if (type === 'MYSQL') bNode.mysqlConfig = {
    dataSourceName: '', dataSourceType: '', sql: '', operationType: 'QUERY',
    outputVariable: '', affectedRowsVariable: ''
  }
  if (type === 'SUB_FLOW') bNode.subFlowConfig = {
    subFlowKey: '', inputMappings: [], outputMappings: []
  }
  if (type === 'LOOP') bNode.loopConfig = {
    arrayVariable: '', itemVariable: '_loop_item', indexVariable: '_loop_index',
    totalVariable: '_loop_total', outputVariable: '_loop_results'
  }
  if (type === 'DELAY' || type === 'WAIT') bNode.delayConfig = {
    delayMs: 1000, variableMode: false, delayVariable: ''
  }
  if (type === 'PARALLEL') bNode.parallelConfig = {
    waitMode: 'ALL_WAIT', timeout: 0, branches: []
  }
  if (type === 'NOTIFY') bNode.notifyConfig = {
    notifyType: 'WEBHOOK', webhookUrl: '', webhookMethod: 'POST',
    webhookHeaders: '', bodyTemplate: '{"flowKey":"${flowKey}","status":"${status}"}',
    emailTo: '', emailSubject: '', failOnError: false
  }
  if (type === 'TRANSFORM') bNode.transformConfig = {
    targetType: 'VARIABLE', targetCode: '', template: ''
  }
  if (type === 'AI') bNode.aiConfig = {
    input: '', systemPrompt: '', output: ''
  }
  if (type === 'FILE_PARSE') bNode.fileParseConfig = {
    input: '', fileType: 'auto', output: ''
  }
  if (type === 'EXCEL_READ') bNode.excelReadConfig = {
    input: '', sheetName: '', output: ''
  }
  if (type === 'FILE_WRITE') bNode.fileWriteConfig = {
    content: '', fileName: '', fileType: 'text', output: ''
  }
  if (type === 'REDIS_GET') bNode.redisGetConfig = {
    key: '', output: ''
  }
  if (type === 'REDIS_SET') bNode.redisSetConfig = {
    key: '', value: '', expireSeconds: 0, output: ''
  }

  businessNodes.value.push(bNode)
  vfNodes.value.push(buildVfNode(bNode, x, y))
  selectNodeByKey(key)
}

function removeNode(key: string) {
  takeSnapshot()
  businessNodes.value = businessNodes.value.filter(n => n.key !== key)
  if (selectedNodeKey.value === key) selectedNodeKey.value = null
  businessNodes.value.forEach(n => {
    n.outgoings = (n.outgoings || []).filter((k: string) => k !== key)
    n.incomings = (n.incomings || []).filter((k: string) => k !== key)
    if (n.conditions) n.conditions.forEach((c: any) => { if (c.outgoing === key) c.outgoing = '' })
  })
  vfNodes.value = vfNodes.value.filter(n => n.id !== key)
  vfEdges.value = vfEdges.value.filter(e => e.source !== key && e.target !== key)
  ElMessage.success(`节点 ${key} 已删除`)
}

function removeEdge(edgeId: string) {
  if (!edgeId) return
  takeSnapshot()
  const edge = vfEdges.value.find(e => e.id === edgeId)
  if (edge) {
    const srcNode = businessNodes.value.find(n => n.key === edge.source)
    const tgtNode = businessNodes.value.find(n => n.key === edge.target)
    if (srcNode) srcNode.outgoings = (srcNode.outgoings || []).filter((k: string) => k !== edge.target)
    if (tgtNode) tgtNode.incomings = (tgtNode.incomings || []).filter((k: string) => k !== edge.source)
    // CONDITION 分支的 outgoing 也清除
    if (srcNode?.elementType === 'CONDITION' && srcNode.conditions) {
      srcNode.conditions.forEach((c: any) => { if (c.outgoing === edge.target) c.outgoing = '' })
    }
  }
  vfEdges.value = vfEdges.value.filter(e => e.id !== edgeId)
  selectedEdgeId.value = null
  ElMessage.success('连线已删除')
}

function onApiSelect(val: any[]) {
  if (!selectedNode.value || selectedNode.value.elementType !== 'METHOD') return
  const [suiteCode, methodCode] = val
  const suiteOption = apiOptions.value.find(s => s.value === suiteCode)
  const apiOption = suiteOption?.children?.find((a: any) => a.value === methodCode)
  if (apiOption?.api) {
    const api = apiOption.api
    Object.assign(selectedNode.value.method, {
      suiteCode, methodCode: api.methodCode, url: api.url,
      requestType: api.requestType, contentType: api.contentType
    })
    // 加载 API 的入参/出参供下拉选择
    loadApiParams(api.id)
  }
}

async function loadApiParams(apiId: number) {
  try {
    const res: any = await request.get(`/suite/api/info/${apiId}`)
    const inputs = res.data?.inputParams || []
    const outputs = res.data?.outputParams || []
    // 递归加载对象属性的子属性
    selectedApiInputParams.value = await buildParamTree(inputs)
    selectedApiOutputParams.value = await buildParamTree(outputs)
  } catch {
    selectedApiInputParams.value = []
    selectedApiOutputParams.value = []
  }
}

async function buildParamTree(params: any[], level = 0, prefix = '', parentPath = ''): Promise<any[]> {
  const result: any[] = []
  for (const p of params) {
    const fullPath = parentPath ? `${parentPath}.${p.paramCode}` : p.paramCode
    result.push({ ...p, _level: level, _prefix: prefix, _displayName: prefix + (p.paramName || p.paramCode), _path: fullPath })
    // 如果是对象或数组类型，且有关联对象，加载子属性
    if ((p.dataType === 'object' || p.dataType === 'array') && p.objectCode) {
      const obj = objectList.value.find((o: any) => o.objectCode === p.objectCode)
      if (obj) {
        try {
          const childRes: any = await request.get('/parameter/list', { params: { ownerId: obj.id, paramType: 3 } })
          const children = childRes.data || []
          const childPrefix = prefix + '\xA0\xA0\xA0\xA0'
          const childTree = await buildParamTree(children, level + 1, childPrefix, fullPath)
          result.push(...childTree)
        } catch {}
      }
    }
  }
  return result
}

function addFillRule(type: 'input' | 'output') {
  if (!selectedNode.value?.method) return
  const rule = {
    source: '', sourceType: type === 'input' ? 'VARIABLE' : 'OUTPUT_PARAM', sourcePath: '',
    target: '', targetType: type === 'input' ? 'INPUT_PARAM' : 'VARIABLE', targetPath: ''
  }
  if (type === 'input') selectedNode.value.method.inputFillRules.push(rule)
  else selectedNode.value.method.outputFillRules.push(rule)
}

function addHeaderRule() {
  if (!selectedNode.value?.method) return
  selectedNode.value.method.headerFillRules.push({ source: '', sourceType: 'CONSTANT', target: '', targetType: 'HEADER' })
}

function addAssignRule() {
  if (!selectedNode.value?.assignRules) return
  selectedNode.value.assignRules.push({ source: '', sourceType: 'CONSTANT', sourcePath: '', target: '', targetType: 'VARIABLE', targetPath: '', dataType: 'string', arrayOpType: '', arrayOpPageNum: '', arrayOpPageSize: '', arrayOpIndex: '' })
}

function onSourceTypeChange(rule: any) {
  rule.sourcePath = ''
  rule.arrayOpType = ''
  rule.arrayOpPageNum = ''
  rule.arrayOpPageSize = ''
  rule.arrayOpIndex = ''
}
function onTargetTypeChange(rule: any) {
  rule.targetPath = ''
  rule.arrayOpType = ''
  rule.arrayOpPageNum = ''
  rule.arrayOpPageSize = ''
  rule.arrayOpIndex = ''
}

// 属性浏览器状态
const propBrowserVisible = ref(false)
const propBrowserRule = ref<any>(null)
const propBrowserMode = ref<'source' | 'target'>('source') // 浏览的是源属性还是目标属性
const propBrowserLevels = ref<any[]>([]) // 级联层级 [{ params: [], selected: '' }]

function browseSource(rule: any) { browseProperties(rule, 'source') }
function browseTarget(rule: any) { browseProperties(rule, 'target') }

async function browseProperties(rule: any, mode: 'source' | 'target') {
  propBrowserRule.value = rule
  propBrowserMode.value = mode
  propBrowserLevels.value = []
  // 确保目标对象存在 targetPath 字段（兼容旧数据）
  if (mode === 'target' && rule.targetPath === undefined) rule.targetPath = ''
  // 根据模式选择要查找的对象code
  const searchCode = mode === 'source' ? rule.source : rule.target
  const srcItem = allComplexTypes.value.find((c: any) => c.code === searchCode)
  if (!srcItem?.objectCode) {
    propBrowserLevels.value = [{ params: [], label: '该参数未关联对象类型' }]
    propBrowserVisible.value = true
    return
  }
  try {
    await loadPropLevel(0, srcItem.objectCode)
  } catch {
    propBrowserLevels.value = [{ params: [], label: '加载属性失败，请检查网络' }]
  }
  propBrowserVisible.value = true
}

async function loadPropLevel(level: number, objectCode: string) {
  // 查找对象 ID
  const obj = objectList.value.find((o: any) => o.objectCode === objectCode)
  if (!obj) { propBrowserLevels.value[level] = { params: [], label: `对象${objectCode}不存在` }; return }
  const res: any = await request.get('/parameter/list', { params: { ownerId: obj.id, paramType: 3 } })
  propBrowserLevels.value[level] = { params: res.data || [], selected: '', level, objectCode }
  // 确保下一级清空
  propBrowserLevels.value = propBrowserLevels.value.slice(0, level + 1)
}

function onPropLevelSelect(level: number, paramCode: string) {
  const currentLevel = propBrowserLevels.value[level]
  if (!currentLevel) return
  currentLevel.selected = paramCode
  // 检查选中的属性是否也是 object/array 类型且有 objectCode
  const selectedParam = currentLevel.params.find((p: any) => p.paramCode === paramCode)
  if (selectedParam && (selectedParam.dataType === 'object' || selectedParam.dataType === 'array') && selectedParam.objectCode) {
    loadPropLevel(level + 1, selectedParam.objectCode)
  } else {
    propBrowserLevels.value = propBrowserLevels.value.slice(0, level + 1)
  }
}

function confirmPropSelection() {
  if (!propBrowserRule.value) return
  const path = propBrowserLevels.value
    .filter((l: any) => l.selected)
    .map((l: any) => l.selected)
    .join('.')
  if (propBrowserMode.value === 'target') {
    propBrowserRule.value.targetPath = path
  } else {
    propBrowserRule.value.sourcePath = path
  }
  propBrowserVisible.value = false
}

// 获取赋值目标占位符文本
function getTargetPlaceholder(targetType: string) {
  switch (targetType) {
    case 'VARIABLE': return '选择变量';
    case 'OUTPUT': return '选择输出参数';
    case 'STATIC': return '选择静态变量';
    case 'INPUT': return '选择入参';
    case 'SUB_PROPERTY': return '选择非简单类型';
    default: return '选择目标';
  }
}
function methodOutputTargetPlaceholder(targetType: string) {
  switch (targetType) {
    case 'VARIABLE': return '选择变量';
    case 'OUTPUT': return '选择输出参数';
    case 'STATIC': return '选择静态变量';
    case 'INPUT': return '选择入参';
    case 'SUB_PROPERTY': return '选择非简单类型';
    default: return '选择目标';
  }
}
function sourceTypeTag(type: string) {
  return { input: '入参', output: '出参', variable: '变量', static: '静态' }[type] || type
}

function addCondition() {
  if (!selectedNode.value?.conditions) return
  selectedNode.value.conditions.push({ conditionName: '新分支', conditionType: 'CUSTOM', expression: '', outgoing: '' })
}

// 流程参数
function addFlowParam(type: 'input' | 'output') {
  const prefix = type === 'input' ? 'input_' : 'output_'
  const param = { paramCode: prefix, paramName: '', dataType: 'string', objectCode: '', required: type === 'input' ? 1 : 0, defaultValue: '', description: '', sortNum: 0 }
  if (type === 'input') flowInputParams.value.push(param)
  else flowOutputParams.value.push(param)
}

async function saveFlowParams(type: 'input' | 'output') {
  if (!flowInfo.value?.id) return
  const paramType = type === 'input' ? 5 : 6
  const params = type === 'input' ? flowInputParams.value : flowOutputParams.value
  const payload = {
    ownerId: flowInfo.value.id,
    ownerCode: flowKey,
    paramType,
    parameters: params.map((p: any, i: number) => ({ ...p, sortNum: i }))
  }
  await request.post('/parameter/save', payload)
  ElMessage.success(`${type === 'input' ? '入参' : '出参'}保存成功`)
  await loadFlowInfo()
}

// 流程参数—来自对象功能
async function openFlowObjectDialog(type: 'input' | 'output') {
  flowObjDialogType.value = type
  selectedFlowObjId.value = null
  flowObjPreviewParams.value = []
  flowObjDialogVisible.value = true
  const res: any = await request.get('/object/list')
  objectList.value = res.data || []
}

async function onFlowObjSelect(objectId: number) {
  const res: any = await request.get('/parameter/list', { params: { ownerId: objectId, paramType: 3 } })
  flowObjPreviewParams.value = (res.data || []).map((p: any) => ({
    ...p, required: p.required ?? 0, defaultValue: p.defaultValue ?? '', description: p.description ?? ''
  }))
}

function importFlowObjParams() {
  const target = flowObjDialogType.value === 'input' ? flowInputParams : flowOutputParams
  for (const p of flowObjPreviewParams.value) {
    target.value.push({
      paramCode: p.paramCode, paramName: p.paramName,
      dataType: p.dataType || 'string', objectCode: p.objectCode ?? '',
      required: flowObjDialogType.value === 'input' ? 1 : 0,
      defaultValue: p.defaultValue ?? '', description: p.description ?? '', sortNum: 0
    })
  }
  flowObjDialogVisible.value = false
  ElMessage.success(`已导入 ${flowObjPreviewParams.value.length} 个参数，请手动保存`)
}

// 变量管理
function addVariable() {
  varDialogVisible.value = true
  varForm.value = { variableCode: '', variableName: '', variableType: 'VARIABLE', dataType: 'string', objectCode: '', defaultValue: '' }
}

function confirmAddVariable() {
  if (!varForm.value.variableCode) { ElMessage.warning('变量Code不能为空'); return }
  if (allVariables.value.some(v => v.variableCode === varForm.value.variableCode)) {
    ElMessage.warning('变量Code已存在'); return
  }
  allVariables.value.push({ ...varForm.value })
  varDialogVisible.value = false
}

async function saveVariables() {
  if (!flowInfo.value?.id) return
  await request.post('/flow/variable/save', {
    flowKey,
    flowDefinitionId: flowInfo.value.id,
    variables: allVariables.value
  })
  ElMessage.success('变量保存成功')
  // 保存成功后重新加载流程信息，确保数据同步
  await loadFlowInfo()
}

// ====== 保存/部署/调试 ======
async function saveFlow() {
  if (!flowInfo.value?.id) return ElMessage.error('流程信息未加载')
  syncVfPositionsToBusinessNodes()
  syncVfEdgesToBusinessNodes()
  await request.put('/flow/definition/save', { id: flowInfo.value.id, flowContent: JSON.stringify(businessNodes.value) })
  ElMessage.success('保存成功')
}

function syncVfEdgesToBusinessNodes() {
  businessNodes.value.forEach(n => { n.outgoings = []; n.incomings = [] })
  vfEdges.value.forEach(edge => {
    const srcKey = edge.source
    const tgtKey = edge.target
    const srcNode = businessNodes.value.find(n => n.key === srcKey)
    const tgtNode = businessNodes.value.find(n => n.key === tgtKey)
    if (srcNode && !srcNode.outgoings.includes(tgtKey)) srcNode.outgoings.push(tgtKey)
    if (tgtNode && !tgtNode.incomings.includes(srcKey)) tgtNode.incomings.push(srcKey)
  })
}

async function deployFlow() {
  await saveFlow()
  await request.post('/flow/definition/deploy', { flowDefinitionId: flowInfo.value?.id })
  ElMessage.success('部署成功')
}

function openDebug() {
  debugVisible.value = true
  debugResult.value = null
  debugTab.value = 'timeline'
}

async function runDebug() {
  debugLoading.value = true
  // 清除旧的高亮
  debugNodeStatus.value = {}
  debugNodeOutput.value = {}
  debugNodeError.value = {}
  try {
    let params = {}
    try { params = JSON.parse(debugParams.value) } catch { ElMessage.error('参数JSON格式错误'); return }
    const res: any = await request.post(`/flow/definition/debug/${flowKey}`, { params })
    debugResult.value = res.data

    // 解析 nodeLogs 并更新节点高亮
    const nodeLogs: any[] = res.data?.nodeLogs || []
    const newStatus: Record<string, string> = {}
    const newOutput: Record<string, any> = {}
    const newError: Record<string, string> = {}
    for (const log of nodeLogs) {
      const k = log.nodeKey
      if (!k) continue
      newStatus[k] = log.status === 'SUCCESS' ? 'success' : 'fail'
      newOutput[k] = log
      if (log.status !== 'SUCCESS') {
        newError[k] = log.errorMessage || log.detail || '执行失败'
      }
    }
    debugNodeStatus.value = newStatus
    debugNodeOutput.value = newOutput
    debugNodeError.value = newError

    // 刷新节点（让 class 重新计算）
    vfNodes.value = vfNodes.value.map(n => ({ ...n }))

    // 自动切换到时间轴
    if (nodeLogs.length) debugTab.value = 'timeline'
    else debugTab.value = 'output'
  } catch (e: any) {
    debugResult.value = { success: false, errorMessage: e.message || '请求失败' }
  } finally {
    debugLoading.value = false
  }
}
</script>

<style scoped>
.designer-container {
  display: flex;
  flex-direction: column;
  height: 100vh;
  background: #1a1a2e;
  outline: none; /* 隐藏 tabindex focus 轮廓 */
}

.toolbar {
  height: 52px;
  background: #001529;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 16px;
  border-bottom: 1px solid #0a2540;
  flex-shrink: 0;
  gap: 8px;
}
.toolbar-left { display: flex; align-items: center; gap: 12px; flex-shrink: 0; }
.toolbar-center { display: flex; gap: 5px; flex-wrap: nowrap; overflow-x: auto; }
.toolbar-right { display: flex; gap: 8px; flex-shrink: 0; }
.flow-title { color: #fff; font-size: 14px; font-weight: 500; white-space: nowrap; }

/* 工具栏按钮颜色 */
.tb-btn-start     { background: #52c41a !important; border-color: #52c41a !important; color: #fff !important; }
.tb-btn-end       { background: #ff4d4f !important; border-color: #ff4d4f !important; color: #fff !important; }
.tb-btn-method    { background: #1890ff !important; border-color: #1890ff !important; color: #fff !important; }
.tb-btn-assign    { background: #722ed1 !important; border-color: #722ed1 !important; color: #fff !important; }
.tb-btn-code      { background: #eb2f96 !important; border-color: #eb2f96 !important; color: #fff !important; }
.tb-btn-mysql     { background: #13c2c2 !important; border-color: #13c2c2 !important; color: #fff !important; }
.tb-btn-condition { background: #fa8c16 !important; border-color: #fa8c16 !important; color: #fff !important; }
.tb-btn-merge     { background: #7c3aed !important; border-color: #7c3aed !important; color: #fff !important; }
.tb-btn-subflow   { background: #0891b2 !important; border-color: #0891b2 !important; color: #fff !important; }
.tb-btn-loop      { background: #8b5cf6 !important; border-color: #8b5cf6 !important; color: #fff !important; }
.tb-btn-delay     { background: #64748b !important; border-color: #64748b !important; color: #fff !important; }
.tb-btn-parallel  { background: #e11d48 !important; border-color: #e11d48 !important; color: #fff !important; }
.tb-btn-notify    { background: #059669 !important; border-color: #059669 !important; color: #fff !important; }

.designer-body { flex: 1; display: flex; overflow: hidden; }

/* 左侧节点工具面板 */
.node-panel {
  width: 110px;
  background: #001529;
  border-right: 1px solid #0a2540;
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  overflow: hidden;
}
.node-panel-title {
  color: rgba(255,255,255,0.65);
  font-size: 12px;
  font-weight: 600;
  padding: 10px 12px 6px;
  text-align: center;
  border-bottom: 1px solid #0a2540;
  flex-shrink: 0;
}
.node-panel-body {
  flex: 1;
  overflow-y: auto;
  padding: 8px 6px;
}
.node-tool-item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 7px 8px;
  border-radius: 6px;
  cursor: pointer;
  margin-bottom: 3px;
  transition: background 0.15s;
  color: rgba(255,255,255,0.8);
  font-size: 12px;
}
.node-tool-item:hover {
  background: rgba(255,255,255,0.08);
}
.nt-icon {
  font-size: 14px;
  width: 20px;
  text-align: center;
  flex-shrink: 0;
}
.nt-label {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
/* 节点工具项颜色指示条 */
.nt-start     { border-left: 3px solid #52c41a; }
.nt-end       { border-left: 3px solid #ff4d4f; }
.nt-method    { border-left: 3px solid #1890ff; }
.nt-sub_flow  { border-left: 3px solid #0891b2; }
.nt-assign    { border-left: 3px solid #722ed1; }
.nt-code      { border-left: 3px solid #eb2f96; }
.nt-mysql     { border-left: 3px solid #13c2c2; }
.nt-condition { border-left: 3px solid #fa8c16; }
.nt-merge     { border-left: 3px solid #7c3aed; }
.nt-loop      { border-left: 3px solid #8b5cf6; }
.nt-delay     { border-left: 3px solid #64748b; }
.nt-parallel  { border-left: 3px solid #e11d48; }
.nt-notify    { border-left: 3px solid #059669; }

/* 中间画布 */
.canvas-area {
  flex: 1;
  position: relative;
  background: #f0f2f5;
  overflow: hidden;
}

.vf-canvas {
  width: 100%;
  height: 100%;
}

.flow-hint {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -60%);
  text-align: center;
  color: #aaa;
  pointer-events: none;
  z-index: 10;
}
.flow-hint p { font-size: 14px; margin-top: 8px; }

/* 删除提示 */
.delete-hint {
  position: absolute;
  bottom: 16px;
  left: 50%;
  transform: translateX(-50%);
  background: rgba(0,0,0,0.65);
  color: #fff;
  padding: 6px 16px;
  border-radius: 20px;
  font-size: 13px;
  pointer-events: none;
  z-index: 20;
  white-space: nowrap;
}
.delete-hint kbd {
  background: rgba(255,255,255,0.2);
  border-radius: 4px;
  padding: 1px 6px;
  font-size: 12px;
  border: 1px solid rgba(255,255,255,0.3);
}

/* 右侧属性面板 */
.right-panel {
  width: 390px;
  background: #fff;
  border-left: 1px solid #eee;
  overflow-y: auto;
  flex-shrink: 0;
}

.panel-title {
  font-size: 13px; font-weight: 600; color: #333;
  padding: 12px 16px; border-bottom: 1px solid #eee;
  background: #f8f9fa; display: flex; align-items: center; gap: 6px;
  position: sticky; top: 0; z-index: 1;
}

.prop-content { padding: 12px; }
.prop-item { margin-bottom: 12px; }
.prop-item label { display: block; font-size: 12px; color: #666; margin-bottom: 4px; font-weight: 500; }
.prop-tip { font-size: 12px; color: #888; background: #f8f9fa; padding: 8px 10px; border-radius: 6px; margin-bottom: 12px; line-height: 1.8; }
.prop-tip code { background: #e8f4fd; padding: 1px 4px; border-radius: 3px; color: #1890ff; font-size: 11px; }
.prop-section-title {
  font-size: 12px; font-weight: 600; color: #444;
  margin: 12px 0 6px; padding-bottom: 4px; border-bottom: 1px solid #eee;
  display: flex; align-items: center;
}

.type-dot-start { color: #52c41a; }
.type-dot-end { color: #ff4d4f; }
.type-dot-method { color: #1890ff; }
.type-dot-assign { color: #722ed1; }
.type-dot-code { color: #eb2f96; }
.type-dot-mysql { color: #13c2c2; }
.type-dot-condition { color: #fa8c16; }
.type-dot-merge { color: #7c3aed; }
.type-dot-sub_flow { color: #0891b2; }
.type-dot-loop { color: #8b5cf6; }
.type-dot-delay { color: #64748b; }
.type-dot-parallel { color: #e11d48; }
.type-dot-notify { color: #059669; }

.fill-rule-row {
  display: flex; align-items: center; gap: 4px;
  margin-bottom: 6px; padding: 6px; background: #fafafa;
  border-radius: 6px; border: 1px solid #f0f0f0;
}
.arrow-icon { color: #1890ff; font-weight: bold; flex-shrink: 0; }

.assign-rule { background: #fafafa; border: 1px solid #f0f0f0; border-radius: 6px; padding: 6px; margin-bottom: 8px; }
.assign-row { display: flex; align-items: center; gap: 4px; }

.condition-item { background: #fffbe6; border: 1px solid #ffe58f; border-radius: 6px; padding: 8px; margin-bottom: 8px; }
.condition-help { max-height: 60vh; overflow-y: auto; }
.condition-help .ch-section { font-weight: 600; margin: 12px 0 4px; color: #303133; }
.condition-help .ch-section:first-child { margin-top: 0; }
.condition-help pre { background: #f6f8fa; border: 1px solid #e4e7ed; border-radius: 6px; padding: 8px 10px; font-size: 12px; line-height: 1.9; margin: 0; overflow-x: auto; font-family: Consolas, Monaco, 'Courier New', monospace; white-space: pre; }
.condition-help .ch-note { color: #909399; font-size: 12px; }
.db-object-list { max-height: 380px; overflow-y: auto; border: 1px solid #e4e7ed; border-radius: 6px; }
.db-object-item { display: flex; align-items: center; justify-content: space-between; padding: 6px 10px; cursor: pointer; border-bottom: 1px solid #f0f2f5; }
.db-object-item:last-child { border-bottom: none; }
.db-object-item:hover { background: #f5f7fa; }
.db-object-item.active { background: #ecf5ff; }
.db-object-name { font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.db-column-list { max-height: 380px; overflow-y: auto; border: 1px solid #e4e7ed; border-radius: 6px; }
.db-column-item { display: flex; align-items: center; justify-content: space-between; padding: 5px 10px; font-size: 12px; border-bottom: 1px solid #f0f2f5; }
.db-column-item:last-child { border-bottom: none; }
.db-test-error { margin-top: 10px; padding: 8px 12px; background: #fef0f0; border: 1px solid #fbc4c4; border-radius: 6px; color: #f56c6c; font-size: 12px; white-space: pre-wrap; word-break: break-all; }
.db-test-result { margin-top: 10px; font-size: 13px; color: #67c23a; }

.code-editor :deep(textarea) { font-family: 'Consolas', 'Monaco', monospace !important; font-size: 12px !important; line-height: 1.6; background: #1e1e1e !important; color: #d4d4d4 !important; }

/* 调试时间轴 */
.debug-timeline-item {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 8px 10px;
  margin-bottom: 6px;
  border-radius: 8px;
  cursor: pointer;
  transition: background 0.15s;
  border: 1px solid transparent;
}
.debug-timeline-item:hover { background: #f0f2f5; }
.dtl-success { border-left: 3px solid #52c41a; background: #f6ffed; }
.dtl-fail    { border-left: 3px solid #ff4d4f; background: #fff1f0; }
.dtl-seq {
  width: 22px; height: 22px; border-radius: 50%; background: #666; color: #fff;
  display: flex; align-items: center; justify-content: center;
  font-size: 11px; font-weight: bold; flex-shrink: 0; margin-top: 2px;
}
.dtl-success .dtl-seq { background: #52c41a; }
.dtl-fail .dtl-seq { background: #ff4d4f; }
.dtl-body { flex: 1; }
.dtl-header { display: flex; align-items: center; gap: 6px; font-size: 13px; }
.dtl-icon { font-size: 14px; }
.dtl-key { font-weight: 600; color: #333; }
.dtl-error { font-size: 12px; color: #ff4d4f; margin-top: 4px; word-break: break-all; }
</style>

<!-- 自定义节点全局样式（非scoped） -->
<style>
/* VueFlow 自定义节点 jg-node */
.jg-node {
  width: 140px;
  min-height: 64px;
  border-radius: 10px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  border: 2px solid transparent;
  padding: 8px 12px;
  box-shadow: 0 2px 10px rgba(0,0,0,0.12);
  background: #fff;
  position: relative;
  transition: box-shadow 0.15s, border-color 0.15s;
  user-select: none;
}
.jg-node:hover { box-shadow: 0 4px 16px rgba(0,0,0,0.2); }
.jg-node.jg-selected { border-color: #1890ff !important; box-shadow: 0 0 0 3px rgba(24,144,255,0.25); }

/* 调试高亮状态 */
.jg-node.jg-debug-success {
  border-color: #52c41a !important;
  box-shadow: 0 0 0 3px rgba(82,196,26,0.3), 0 0 12px rgba(82,196,26,0.4) !important;
}
.jg-node.jg-debug-fail {
  border-color: #ff4d4f !important;
  box-shadow: 0 0 0 3px rgba(255,77,79,0.3), 0 0 12px rgba(255,77,79,0.4) !important;
}
.jg-node.jg-debug-running {
  border-color: #faad14 !important;
  box-shadow: 0 0 0 3px rgba(250,173,20,0.3) !important;
  animation: pulse-running 1s infinite;
}
@keyframes pulse-running {
  0%, 100% { box-shadow: 0 0 0 3px rgba(250,173,20,0.3); }
  50% { box-shadow: 0 0 0 6px rgba(250,173,20,0.5); }
}

/* 调试状态角标 */
.jg-debug-badge {
  position: absolute;
  top: -8px;
  right: -8px;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  font-weight: bold;
  z-index: 10;
}
.jg-debug-success .jg-debug-badge { background: #52c41a; color: #fff; }
.jg-debug-fail .jg-debug-badge { background: #ff4d4f; color: #fff; }
.jg-debug-running .jg-debug-badge { background: #faad14; color: #fff; }

/* 调试输出按钮 */
.jg-debug-output {
  font-size: 10px;
  color: #1890ff;
  margin-top: 3px;
  cursor: pointer;
  text-decoration: underline;
  padding: 1px 4px;
  border-radius: 3px;
  background: rgba(24,144,255,0.08);
}
.jg-debug-output:hover { background: rgba(24,144,255,0.18); }

/* 节点报错提示 */
.jg-debug-error-tip {
  font-size: 10px;
  color: #ff4d4f;
  margin-top: 3px;
  cursor: pointer;
  padding: 2px 5px;
  border-radius: 3px;
  background: rgba(255,77,79,0.08);
  border: 1px solid rgba(255,77,79,0.2);
  max-width: 140px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  line-height: 1.4;
}
.jg-debug-error-tip:hover { background: rgba(255,77,79,0.15); }

.jg-icon { font-size: 20px; margin-bottom: 4px; }
.jg-name { font-size: 12px; font-weight: 600; color: #333; max-width: 120px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; text-align: center; }
.jg-type { font-size: 11px; color: #999; }

.jg-start     { background: linear-gradient(135deg, #f6ffed, #d9f7be); border-color: #52c41a; }
.jg-end       { background: linear-gradient(135deg, #fff1f0, #ffccc7); border-color: #ff4d4f; }
.jg-method    { background: linear-gradient(135deg, #e6f4ff, #bae0ff); border-color: #1890ff; }
.jg-assign    { background: linear-gradient(135deg, #f9f0ff, #d3adf7); border-color: #722ed1; }
.jg-code      { background: linear-gradient(135deg, #fff0f6, #ffadd2); border-color: #eb2f96; }
.jg-mysql     { background: linear-gradient(135deg, #e6fffb, #b5f5ec); border-color: #13c2c2; }
.jg-condition { background: linear-gradient(135deg, #fff7e6, #ffd591); border-color: #fa8c16; }
.jg-merge     { background: linear-gradient(135deg, #f5f0ff, #c4b5fd); border-color: #7c3aed; }
.jg-sub_flow  { background: linear-gradient(135deg, #e0f2fe, #7dd3fc); border-color: #0891b2; }
.jg-loop      { background: linear-gradient(135deg, #f5f3ff, #c4b5fd); border-color: #8b5cf6; }
.jg-delay     { background: linear-gradient(135deg, #f1f5f9, #cbd5e1); border-color: #64748b; }
.jg-parallel  { background: linear-gradient(135deg, #fff1f2, #fecdd3); border-color: #e11d48; }
.jg-notify    { background: linear-gradient(135deg, #ecfdf5, #a7f3d0); border-color: #059669; }

/* Handle 连接点样式 */
.jg-handle {
  width: 10px !important;
  height: 10px !important;
  background: #1890ff !important;
  border: 2px solid #fff !important;
  border-radius: 50% !important;
}
.jg-handle-top  { top: -6px !important; }
.jg-handle-bottom { bottom: -6px !important; }

/* VueFlow 画布背景 */
.vue-flow__background { background: #f0f2f5; }

/* 覆盖 VueFlow 控件颜色 */
.vue-flow__controls-button {
  background: #fff;
  border: 1px solid #ddd;
  color: #333;
}
.vue-flow__controls-button:hover { background: #e6f4ff; }

/* 选中的边高亮 */
.vue-flow__edge.selected .vue-flow__edge-path {
  stroke: #ff4d4f !important;
  stroke-width: 3px !important;
}
</style>
