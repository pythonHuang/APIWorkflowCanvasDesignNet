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

          <!-- NOTIFY 通知节点属性 -->
          <template v-if="selectedNode.elementType === 'NOTIFY'">
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
              <el-button size="small" icon="Delete" circle type="danger" @click="selectedNode.method!.headerFillRules.splice(i, 1)" />
            </div>

            <!-- 输入参数配置 -->
            <div class="prop-section-title">
              输入参数（Body/Query）
              <el-button size="small" icon="Plus" link @click="addFillRule('input')" style="margin-left:auto">添加</el-button>
            </div>
            <div v-for="(rule, i) in selectedNode.method?.inputFillRules" :key="'i'+i" class="fill-rule-row">
              <el-select v-model="rule.sourceType" size="small" style="width:70px;flex-shrink:0">
                <el-option value="VARIABLE" label="变量" />
                <el-option value="CONSTANT" label="常量" />
                <el-option value="STATIC" label="静态" />
                <el-option value="INPUT" label="入参" />
              </el-select>
              <el-input v-if="rule.sourceType==='CONSTANT'" v-model="rule.source" placeholder="常量值" size="small" style="flex:1" />
              <el-select v-else-if="rule.sourceType==='STATIC'" v-model="rule.source" placeholder="选择静态变量" size="small" style="flex:1">
                <el-option v-for="s in staticVariables" :key="s.varCode" :value="s.varCode" :label="`${s.varName} (${s.varCode})`" />
              </el-select>
              <el-select v-else-if="rule.sourceType==='INPUT'" v-model="rule.source" placeholder="选择入参" size="small" style="flex:1">
                <el-option v-for="p in flowInputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
              </el-select>
              <el-select v-else v-model="rule.source" placeholder="来源变量" size="small" style="flex:1">
                <el-option v-for="v in allVariables" :key="v.variableCode" :value="v.variableCode" :label="v.variableCode" />
              </el-select>
              <span class="arrow-icon">→</span>
              <el-select v-model="rule.target" placeholder="API入参名" size="small" style="width:36%" filterable allow-create default-first-option>
                <el-option v-for="p in selectedApiInputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
              </el-select>
              <el-button size="small" icon="Delete" circle type="danger" @click="selectedNode.method!.inputFillRules.splice(i, 1)" />
            </div>

            <!-- 输出参数配置 -->
            <div class="prop-section-title">
              输出映射（Response→变量）
              <el-button size="small" icon="Plus" link @click="addFillRule('output')" style="margin-left:auto">添加</el-button>
            </div>
            <div v-for="(rule, i) in selectedNode.method?.outputFillRules" :key="'o'+i" class="fill-rule-row">
              <el-select v-model="rule.source" placeholder="响应字段path" size="small" style="flex:1" filterable allow-create default-first-option>
                <el-option v-for="p in selectedApiOutputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
              </el-select>
              <span class="arrow-icon">→</span>
              <el-select v-model="rule.targetType" size="small" style="width:80px;flex-shrink:0">
                <el-option value="VARIABLE" label="变量" />
                <el-option value="OUTPUT" label="出参" />
                <el-option value="STATIC" label="静态" />
                <el-option value="INPUT" label="入参" />
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
              </el-select>
              <el-button size="small" icon="Delete" circle type="danger" @click="selectedNode.method!.outputFillRules.splice(i, 1)" />
            </div>
          </template>

          <!-- ASSIGN 节点属性 -->
          <template v-if="selectedNode.elementType === 'ASSIGN'">
            <div class="prop-tip">赋值节点：将常量或变量赋值给目标变量。</div>
            <div class="prop-section-title">
              赋值规则
              <el-button size="small" icon="Plus" link @click="addAssignRule" style="margin-left:auto">添加</el-button>
            </div>
            <div v-for="(rule, i) in selectedNode.assignRules" :key="i" class="assign-rule">
              <div class="assign-row">
                <el-select v-model="rule.sourceType" size="small" style="width:80px;flex-shrink:0">
                  <el-option value="CONSTANT" label="常量" />
                  <el-option value="VARIABLE" label="变量" />
                  <el-option value="STATIC" label="静态" />
                  <el-option value="INPUT" label="入参" />
                  <el-option value="INPUT_PROPERTY" label="入参子对象" />
                </el-select>
                <template v-if="rule.sourceType === 'CONSTANT'">
                  <el-input v-model="rule.source" placeholder="常量值" size="small" style="flex:1" />
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
                <template v-else-if="rule.sourceType === 'INPUT_PROPERTY'">
                  <el-select v-model="rule.source" placeholder="选择入参" size="small" style="flex:1">
                    <el-option v-for="p in flowInputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                  </el-select>
                </template>
              </div>
              <div class="assign-row" style="margin-top:4px">
                <span style="font-size:12px;color:#666;width:72px;flex-shrink:0">→ 赋值给</span>
                <el-select v-model="rule.targetType" size="small" style="width:80px;flex-shrink:0">
                  <el-option value="VARIABLE" label="变量" />
                  <el-option value="OUTPUT" label="出参" />
                  <el-option value="STATIC" label="静态" />
                  <el-option value="INPUT" label="入参" />
                  <el-option value="OUTPUT_PROPERTY" label="出参子对象" />
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
                  <template v-else-if="rule.targetType === 'OUTPUT_PROPERTY'">
                    <el-option v-for="p in flowOutputParams" :key="p.paramCode" :value="p.paramCode" :label="`${p.paramName} (${p.paramCode})`" />
                  </template>
                </el-select>
                <el-select v-model="rule.dataType" size="small" style="width:72px;flex-shrink:0">
                  <el-option value="string" label="string" />
                  <el-option value="integer" label="integer" />
                  <el-option value="double" label="double" />
                  <el-option value="boolean" label="bool" />
                </el-select>
                <el-button size="small" icon="Delete" circle type="danger" @click="selectedNode.assignRules!.splice(i, 1)" />
              </div>
            </div>
          </template>

          <!-- CODE 节点属性 -->
          <template v-if="selectedNode.elementType === 'CODE'">
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
            <div class="prop-tip">数据库节点：执行 SQL，支持 <code>${varName}</code> 模板变量。</div>
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
            <div class="prop-tip">条件节点：每个分支连接到不同的目标节点（通过画布连线），在此设置判断表达式。</div>
            <div class="prop-section-title">
              条件分支
              <el-button size="small" icon="Plus" link @click="addCondition" style="margin-left:auto">添加</el-button>
            </div>
            <div v-for="(cond, i) in selectedNode.conditions" :key="i" class="condition-item">
              <div style="display:flex;gap:4px;align-items:center;margin-bottom:4px">
                <el-input v-model="cond.conditionName" placeholder="分支名称" size="small" style="flex:1" />
                <el-select v-model="cond.conditionType" size="small" style="width:90px;flex-shrink:0">
                  <el-option value="CUSTOM" label="自定义" />
                  <el-option value="DEFAULT" label="默认(else)" />
                </el-select>
                <el-button size="small" icon="Delete" circle type="danger" @click="selectedNode.conditions!.splice(i, 1)" />
              </div>
              <el-input v-if="cond.conditionType === 'CUSTOM'"
                v-model="cond.expression"
                placeholder="如: score >= 60 或 status == 'active'"
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
            <el-table-column label="类型" width="95">
              <template #default="{ row }">
                <el-select v-model="row.dataType" size="small" style="width:100%">
                  <el-option v-for="t in dataTypes" :key="t.value" :value="t.value" :label="t.label" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="必填" width="55" align="center">
              <template #default="{ row }">
                <el-checkbox v-model="row.required" :true-value="1" :false-value="0" />
              </template>
            </el-table-column>
            <el-table-column label="默认值" width="90">
              <template #default="{ row }">
                <el-input v-model="row.defaultValue" size="small" />
              </template>
            </el-table-column>
            <el-table-column label="描述">
              <template #default="{ row }">
                <el-input v-model="row.description" size="small" />
              </template>
            </el-table-column>
            <el-table-column label="" width="50" align="center">
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
            <el-table-column label="类型" width="95">
              <template #default="{ row }">
                <el-select v-model="row.dataType" size="small" style="width:100%">
                  <el-option v-for="t in dataTypes" :key="t.value" :value="t.value" :label="t.label" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="描述">
              <template #default="{ row }">
                <el-input v-model="row.description" size="small" />
              </template>
            </el-table-column>
            <el-table-column label="" width="50" align="center">
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
          <el-table-column label="数据类型" width="95">
            <template #default="{ row }">
              <el-select v-model="row.dataType" size="small" style="width:100%">
                <el-option v-for="t in dataTypes" :key="t.value" :value="t.value" :label="t.label" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="默认值">
            <template #default="{ row }">
              <el-input v-model="row.defaultValue" size="small" placeholder="可选" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="55">
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

    <!-- ========== 调试弹窗 ========== -->
    <el-dialog v-model="debugVisible" title="🐛 流程调试" width="780px" :close-on-click-modal="false">
      <div style="margin-bottom:8px;color:#666;font-size:13px">
        已定义的入参：
        <el-tag v-for="p in flowInputParams" :key="p.paramCode" size="small" style="margin-right:4px">
          {{ p.paramCode }}({{ p.dataType }})
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
const varForm = ref({ variableCode: '', variableName: '', variableType: 'VARIABLE', dataType: 'string', defaultValue: '' })

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
  await Promise.all([loadFlowInfo(), loadSuiteApis(), loadDataSources(), loadStaticVariables(), loadPublishedFlows()])
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
    LOOP: '↻', DELAY: '⏱', PARALLEL: '∥', NOTIFY: '✉', TRANSFORM: '📝'
  }
  return map[type] || '?'
}

function nodeTypeName(type: string) {
  const map: Record<string, string> = {
    START: '开始', END: '结束', METHOD: '方法', CONDITION: '条件',
    ASSIGN: '赋值', CODE: '代码', MYSQL: '数据库', MERGE: '聚合', SUB_FLOW: '子流程',
    LOOP: '循环', DELAY: '延迟', PARALLEL: '并行', NOTIFY: '通知', TRANSFORM: '模板转换'
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
    selectedApiInputParams.value = res.data?.inputParams || []
    selectedApiOutputParams.value = res.data?.outputParams || []
  } catch {
    selectedApiInputParams.value = []
    selectedApiOutputParams.value = []
  }
}

function addFillRule(type: 'input' | 'output') {
  if (!selectedNode.value?.method) return
  const rule = {
    source: '', sourceType: type === 'input' ? 'VARIABLE' : 'OUTPUT_PARAM',
    target: '', targetType: type === 'input' ? 'INPUT_PARAM' : 'VARIABLE'
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
  selectedNode.value.assignRules.push({ source: '', sourceType: 'CONSTANT', target: '', targetType: 'VARIABLE', dataType: 'string' })
}

// 获取赋值目标占位符文本
function getTargetPlaceholder(targetType: string) {
  switch (targetType) {
    case 'VARIABLE': return '选择变量';
    case 'OUTPUT': return '选择输出参数';
    case 'STATIC': return '选择静态变量';
    case 'INPUT': return '选择入参';
    case 'INPUT_PROPERTY': return '选择入参子对象';
    case 'OUTPUT_PROPERTY': return '选择出参子对象';
    default: return '选择目标';
  }
}
function methodOutputTargetPlaceholder(targetType: string) {
  switch (targetType) {
    case 'VARIABLE': return '选择变量';
    case 'OUTPUT': return '选择输出参数';
    case 'STATIC': return '选择静态变量';
    case 'INPUT': return '选择入参';
    default: return '选择目标';
  }
}

function addCondition() {
  if (!selectedNode.value?.conditions) return
  selectedNode.value.conditions.push({ conditionName: '新分支', conditionType: 'CUSTOM', expression: '', outgoing: '' })
}

// 流程参数
function addFlowParam(type: 'input' | 'output') {
  const prefix = type === 'input' ? 'input_' : 'output_'
  const param = { paramCode: prefix, paramName: '', dataType: 'string', required: type === 'input' ? 1 : 0, defaultValue: '', description: '', sortNum: 0 }
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
      dataType: p.dataType || 'string', required: flowObjDialogType.value === 'input' ? 1 : 0,
      defaultValue: p.defaultValue ?? '', description: p.description ?? '', sortNum: 0
    })
  }
  flowObjDialogVisible.value = false
  ElMessage.success(`已导入 ${flowObjPreviewParams.value.length} 个参数，请手动保存`)
}

// 变量管理
function addVariable() {
  varDialogVisible.value = true
  varForm.value = { variableCode: '', variableName: '', variableType: 'VARIABLE', dataType: 'string', defaultValue: '' }
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
