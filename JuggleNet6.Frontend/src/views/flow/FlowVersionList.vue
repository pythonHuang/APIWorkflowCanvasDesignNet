<template>
  <div class="page-container">
    <div class="page-header">
      <div>
        <el-button icon="ArrowLeft" link @click="router.back()">返回</el-button>
        <h2 style="display:inline;margin-left:8px">版本管理 - {{ flowKey }}</h2>
      </div>
      <el-button type="warning" @click="openDiffDialog" icon="Switch" :disabled="tableData.length < 2">版本对比</el-button>
    </div>
    <el-card class="table-card">
      <el-table :data="tableData" stripe v-loading="loading" height="100%">
        <el-table-column prop="version" label="版本号" width="100" />
        <el-table-column prop="status" label="状态" width="80">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'danger'" size="small">
              {{ row.status === 1 ? '启用' : '禁用' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" width="180" show-overflow-tooltip />
        <el-table-column label="操作" width="260" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" link @click="triggerTest(row)">触发测试</el-button>
            <el-button size="small" link @click="viewContent(row)">查看JSON</el-button>
            <el-tooltip content="复制此版本调用地址">
              <el-button size="small" link icon="CopyDocument" @click="copyVersionUrl(row)" />
            </el-tooltip>
            <el-tooltip content="打开此版本 WSDL">
              <el-button size="small" link @click="openWsdl(row)">WSDL</el-button>
            </el-tooltip>
            <el-button size="small" :type="row.status === 1 ? 'warning' : 'success'" link
              @click="toggleStatus(row)">
              {{ row.status === 1 ? '禁用' : '启用' }}
            </el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 版本对比对话框 -->
    <el-dialog v-model="diffVisible" title="流程版本对比" width="90%" top="5vh" destroy-on-close>
      <div style="display:flex;gap:12px;margin-bottom:16px;align-items:center">
        <el-select v-model="diffLeft" placeholder="选择左侧版本" style="flex:1" size="small">
          <el-option v-for="v in tableData" :key="v.id" :value="v.id" :label="'v' + v.version + ' (' + v.createdAt?.substring(0,16) + ')'" />
        </el-select>
        <el-icon style="font-size:20px;color:#999"><Switch /></el-icon>
        <el-select v-model="diffRight" placeholder="选择右侧版本" style="flex:1" size="small">
          <el-option v-for="v in tableData" :key="v.id" :value="v.id" :label="'v' + v.version + ' (' + v.createdAt?.substring(0,16) + ')'" />
        </el-select>
        <el-button type="primary" size="small" @click="doDiff" :disabled="diffLeft === diffRight || !diffLeft || !diffRight">对比</el-button>
      </div>
      <div v-if="diffResult" class="diff-container">
        <div class="diff-summary">
          <el-tag type="success" size="small">新增 {{ diffResult.added }} 行</el-tag>
          <el-tag type="danger" size="small">删除 {{ diffResult.removed }} 行</el-tag>
          <el-tag type="warning" size="small">修改 {{ diffResult.changed }} 行</el-tag>
        </div>
        <div class="diff-code">
          <div v-for="(line, i) in diffResult.lines" :key="i" :class="'diff-line diff-line-' + line.type">
            <span class="diff-linenum">{{ Number(i) + 1 }}</span>
            <span class="diff-prefix">{{ line.type === 'added' ? '+' : line.type === 'removed' ? '-' : ' ' }}</span>
            <span class="diff-text">{{ line.text }}</span>
          </div>
        </div>
      </div>
    </el-dialog>

    <!-- 触发测试对话框 -->
    <el-dialog v-model="testVisible" title="触发测试" width="700px" destroy-on-close>
      <div style="margin-bottom:10px;color:#666;font-size:13px">
        版本: <el-tag size="small" type="warning">{{ testVersion?.version }}</el-tag>
        &nbsp;FlowKey: <el-tag size="small">{{ flowKey }}</el-tag>
      </div>
      <div v-if="testInputParams.length > 0" style="margin-bottom:8px">
        <span style="font-size:13px;color:#666">已定义的入参：</span>
        <el-tag v-for="p in testInputParams" :key="p.paramCode" size="small" style="margin-right:4px">
          {{ p.paramCode }}({{ p.dataType }})
        </el-tag>
      </div>
      <div v-else style="margin-bottom:8px;font-size:13px;color:#aaa">该流程未定义入参</div>
      <el-form label-width="80px">
        <el-form-item label="输入参数">
          <el-input v-model="testParamsJson" type="textarea" :rows="6"
            placeholder='{"input_name": "张三", "input_age": 25}' class="code-editor" />
        </el-form-item>
      </el-form>
      <div v-if="testResult !== null" style="margin-top:12px">
        <el-divider />
        <div style="display:flex;align-items:center;gap:12px;margin-bottom:10px">
          <span style="font-weight:bold;font-size:15px" :style="{ color: testSuccess ? '#52c41a' : '#ff4d4f' }">
            {{ testSuccess ? '执行成功' : '执行失败' }}
          </span>
          <span v-if="testCostMs" style="color:#888;font-size:12px">耗时 {{ testCostMs }} ms</span>
          <span v-if="testLogId" style="color:#888;font-size:12px">LogId: {{ testLogId }}</span>
        </div>
        <el-input v-model="testResult" type="textarea" :rows="12" readonly style="font-family:monospace;font-size:13px" />
      </div>
      <template #footer>
        <el-button @click="testVisible = false">关闭</el-button>
        <el-button type="primary" :loading="testLoading" @click="executeTriggerTest">发送请求</el-button>
      </template>
    </el-dialog>

    <!-- 查看JSON对话框 -->
    <el-dialog v-model="jsonVisible" title="流程节点 JSON" width="70%" destroy-on-close>
      <div style="display:flex;justify-content:flex-end;margin-bottom:8px">
        <el-button size="small" @click="formatJson" icon="MagicStick">格式化</el-button>
        <el-button size="small" @click="copyJson" icon="CopyDocument">复制</el-button>
      </div>
      <el-input v-model="jsonContent" type="textarea" :rows="20" readonly style="font-family:monospace;font-size:13px" />
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import request from '../../utils/request'

const route = useRoute()
const router = useRouter()
const flowKey = route.params.flowKey as string
const loading = ref(false)
const tableData = ref<any[]>([])

// 版本对比
const diffVisible = ref(false)
const diffLeft = ref<number>(0)
const diffRight = ref<number>(0)
const diffResult = ref<any>(null)

// 触发测试
const testVisible = ref(false)
const testLoading = ref(false)
const testVersion = ref<any>(null)
const testInputParams = ref<any[]>([])
const testParamsJson = ref('{}')
const testResult = ref<string | null>(null)
const testSuccess = ref(false)
const testCostMs = ref(0)
const testLogId = ref(0)

// 查看JSON
const jsonVisible = ref(false)
const jsonContent = ref('')

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.post('/flow/version/page', { pageNum: 1, pageSize: 100, flowKey })
    tableData.value = res.data.records
  } finally { loading.value = false }
}

async function toggleStatus(row: any) {
  const newStatus = row.status === 1 ? 0 : 1
  await request.put('/flow/version/status', { id: row.id, status: newStatus })
  ElMessage.success(newStatus === 1 ? '已启用' : '已禁用')
  loadData()
}

async function doDelete(row: any) {
  await ElMessageBox.confirm(`确认删除版本 ${row.version}？`, '提示', { type: 'warning' })
  await request.delete(`/flow/version/delete/${row.id}`)
  ElMessage.success('删除成功')
  loadData()
}

async function triggerTest(row: any) {
  testVersion.value = row
  testParamsJson.value = '{}'
  testResult.value = null
  testSuccess.value = false
  testCostMs.value = 0
  testLogId.value = 0
  testVisible.value = true
  try {
    const infoRes: any = await request.get(`/flow/definition/infoByKey/${flowKey}`)
    testInputParams.value = infoRes.data?.inputParams || []
    if (testInputParams.value.length > 0) {
      const defaults: Record<string, any> = {}
      for (const p of testInputParams.value) {
        defaults[p.paramCode] = p.defaultValue || ''
      }
      testParamsJson.value = JSON.stringify(defaults, null, 2)
    }
  } catch { testInputParams.value = [] }
}

async function executeTriggerTest() {
  if (!testVersion.value) return
  testLoading.value = true
  testResult.value = null
  try {
    let params: any = {}
    try { params = JSON.parse(testParamsJson.value) } catch { ElMessage.warning('输入参数JSON格式错误'); testLoading.value = false; return }
    const res: any = await request.post(`/flow/version/trigger/${testVersion.value.version}/${flowKey}`, { params })
    testSuccess.value = res.data?.success !== false
    testCostMs.value = res.data?.costMs || 0
    testLogId.value = res.data?.logId || 0
    testResult.value = JSON.stringify(res.data, null, 2)
  } catch (e: any) {
    testSuccess.value = false
    testResult.value = e?.message || '请求异常'
  } finally { testLoading.value = false }
}

function copyToClipboard(text: string) {
  if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
    navigator.clipboard.writeText(text).then(() => {
      ElMessage.success('已复制到剪贴板')
    }).catch(() => { ElMessage.error('复制失败') })
    return
  }
  // 降级方案：适用于非 HTTPS 环境
  try {
    const ta = document.createElement('textarea')
    ta.value = text
    ta.style.position = 'fixed'
    ta.style.left = '-9999px'
    document.body.appendChild(ta)
    ta.focus()
    ta.select()
    document.execCommand('copy')
    document.body.removeChild(ta)
    ElMessage.success('已复制到剪贴板')
  } catch {
    ElMessage.error('复制失败，请手动复制')
  }
}

function copyVersionUrl(row: any) {
  const baseUrl = window.location.origin
  const url = `${baseUrl}/open/flow/trigger/${row.version}/${flowKey}\nMethod: POST\nHeader: X-Access-Token: <your-token>\nBody: {"flowData": {}}`
  copyToClipboard(url)
}

function openWsdl(row: any) {
  const baseUrl = window.location.origin
  window.open(`${baseUrl}/open/flow/wsdl/${row.version}/${flowKey}`, '_blank')
}

function openDiffDialog() {
  if (tableData.value.length >= 2) {
    diffLeft.value = tableData.value[1]?.id
    diffRight.value = tableData.value[0]?.id
  }
  diffResult.value = null
  diffVisible.value = true
}

async function doDiff() {
  const leftRow = tableData.value.find((v: any) => v.id === diffLeft.value)
  const rightRow = tableData.value.find((v: any) => v.id === diffRight.value)
  if (!leftRow || !rightRow) return

  try {
    // 从版本列表中获取 flowContent（可能需要重新加载）
    const res: any = await request.post('/flow/version/page', { pageNum: 1, pageSize: 100, flowKey })
    const allVersions = res.data?.records || []
    const leftVer = allVersions.find((v: any) => v.id === diffLeft.value)
    const rightVer = allVersions.find((v: any) => v.id === diffRight.value)

    const leftJson = leftVer?.flowContent ? formatFlowJson(leftVer.flowContent) : ''
    const rightJson = rightVer?.flowContent ? formatFlowJson(rightVer.flowContent) : ''

    diffResult.value = computeDiff(leftJson, rightJson)
  } catch (e: any) {
    ElMessage.error('对比失败: ' + (e.message || e))
  }
}

function formatFlowJson(content: string): string {
  try {
    const nodes = JSON.parse(content)
    if (Array.isArray(nodes)) {
      // 格式化为每行一个节点的可读格式
      return nodes.map((n: any) => {
        const label = n.label || n.key
        const type = n.elementType
        const outgoings = n.outgoings?.length ? ' → [' + n.outgoings.join(',') + ']' : ''
        const incomings = n.incomings?.length ? ' ← [' + n.incomings.join(',') + ']' : ''
        return `${n.key} [${type}] "${label}"${incomings}${outgoings}`
      }).join('\n')
    }
    return JSON.stringify(nodes, null, 2)
  } catch {
    return content
  }
}

function computeDiff(left: string, right: string) {
  const leftLines = left.split('\n')
  const rightLines = right.split('\n')

  // 简单 LCS diff 算法
  const lines: any[] = []
  const leftSet = new Set(leftLines)
  const rightSet = new Set(rightLines)
  let added = 0, removed = 0, changed = 0

  // 构建统一的行列表
  // 逐行对比
  for (const line of leftLines) {
    if (!rightSet.has(line)) {
      lines.push({ type: 'removed', text: line })
      removed++
    } else {
      lines.push({ type: 'unchanged', text: line })
    }
  }
  for (const line of rightLines) {
    if (!leftSet.has(line)) {
      lines.push({ type: 'added', text: line })
      added++
    }
  }

  return { lines, added, removed, changed }
}

function viewContent(row: any) {
  // 重新加载以获取完整 flowContent
  request.post('/flow/version/page', { pageNum: 1, pageSize: 100, flowKey }).then((res: any) => {
    const ver = res.data?.records?.find((v: any) => v.id === row.id)
    jsonContent.value = ver?.flowContent ? JSON.stringify(JSON.parse(ver.flowContent), null, 2) : '(无内容)'
    jsonVisible.value = true
  })
}

function formatJson() {
  try {
    jsonContent.value = JSON.stringify(JSON.parse(jsonContent.value), null, 2)
  } catch {
    ElMessage.warning('JSON 格式错误，无法格式化')
  }
}

function copyJson() {
  copyToClipboard(jsonContent.value)
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
}
.table-card :deep(.el-table) {
  flex: 1;
  min-height: 0;
}
.diff-summary { display: flex; gap: 8px; margin-bottom: 12px; }
.diff-code { background: #1e1e1e; color: #d4d4d4; border-radius: 8px; padding: 12px; font-family: 'Consolas', 'Monaco', monospace; font-size: 13px; overflow: auto; max-height: 60vh; }
.diff-line { display: flex; gap: 8px; line-height: 1.6; padding: 0 4px; }
.diff-line-added { background: #1e3a1e; }
.diff-line-removed { background: #3a1e1e; }
.diff-line-unchanged { color: #808080; }
.diff-linenum { color: #6e7681; min-width: 36px; text-align: right; user-select: none; }
.diff-prefix { min-width: 16px; font-weight: bold; }
.diff-line-added .diff-prefix { color: #4ec9b0; }
.diff-line-removed .diff-prefix { color: #f44747; }
.diff-text { white-space: pre-wrap; word-break: break-all; }
.code-editor textarea { font-family: 'Consolas','Monaco',monospace; font-size: 13px; }
</style>
