<template>
  <div class="page-container">
    <div class="page-header">
      <div>
        <el-button icon="ArrowLeft" link @click="router.back()">返回</el-button>
        <h2 style="display:inline;margin-left:8px">接口管理 - {{ suiteCode }}</h2>
      </div>
      <div>
        <el-button size="small" @click="batchGenerateVisible = true">批量生成</el-button>
        <el-button size="small" @click="importVisible = true">导入</el-button>
        <el-button size="small" :disabled="selectedIds.length === 0" @click="doExport">导出选中</el-button>
        <el-button type="primary" icon="Plus" @click="openAdd">新建接口</el-button>
      </div>
    </div>

    <el-card class="table-card">
      <el-table :data="tableData" stripe v-loading="loading" height="100%" @selection-change="onSelectionChange">
        <el-table-column type="selection" width="40" />
        <el-table-column prop="methodCode" label="接口Code" width="220" show-overflow-tooltip />
        <el-table-column prop="methodName" label="接口名称" />
        <el-table-column label="类型" width="110">
          <template #default="{ row }">
            <el-tag :type="row.methodType === 'WEBSERVICE' ? 'warning' : 'info'" size="small">
              {{ row.methodType === 'WEBSERVICE' ? 'WebService' : 'HTTP' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="requestType" label="请求方式" width="90">
          <template #default="{ row }">
            <el-tag v-if="row.methodType !== 'WEBSERVICE'" :type="methodColor(row.requestType)" size="small">{{ row.requestType }}</el-tag>
            <el-tag v-else type="warning" size="small">SOAP</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="url" label="URL" show-overflow-tooltip />
        <el-table-column label="操作" width="280" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" link @click="openDetail(row)">详情/参数</el-button>
            <el-button size="small" link @click="openEdit(row)">编辑</el-button>
            <el-button size="small" type="success" link @click="openTest(row)">测试</el-button>
            <el-button size="small" type="warning" link @click="copyCurl(row)">cURL</el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="isEdit ? '编辑接口' : '新建接口'" width="600px">
      <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
        <el-form-item label="接口名称" prop="methodName">
          <el-input v-model="form.methodName" />
        </el-form-item>
        <el-form-item label="接口类型">
          <el-radio-group v-model="form.methodType">
            <el-radio value="HTTP">API 接口（HTTP）</el-radio>
            <el-radio value="WEBSERVICE">WebService（SOAP）</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="form.methodType === 'HTTP'" label="请求方式" prop="requestType">
          <el-radio-group v-model="form.requestType">
            <el-radio value="GET">GET</el-radio>
            <el-radio value="POST">POST</el-radio>
            <el-radio value="PUT">PUT</el-radio>
            <el-radio value="DELETE">DELETE</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="URL" prop="url">
          <el-input v-model="form.url" :placeholder="form.methodType === 'WEBSERVICE' ? 'http://...?wsdl' : 'http://...'" />
        </el-form-item>
        <!-- WebService 专用字段 -->
        <template v-if="form.methodType === 'WEBSERVICE'">
          <el-form-item label="SOAP 版本">
            <el-radio-group v-model="form.soapVersion">
              <el-radio value="11">SOAP 1.1</el-radio>
              <el-radio value="12">SOAP 1.2</el-radio>
            </el-radio-group>
          </el-form-item>
          <el-form-item label="操作名称">
            <el-input v-model="form.soapMethod" placeholder="如: SayHello" />
          </el-form-item>
          <el-form-item label="命名空间">
            <el-input v-model="form.soapNamespace" placeholder="如: http://example.com/ws/" />
          </el-form-item>
          <el-form-item label="SOAPAction">
            <el-input v-model="form.soapAction" placeholder="如: urn:example/SayHello" />
          </el-form-item>
        </template>
        <el-form-item v-if="form.methodType === 'HTTP'" label="内容类型">
          <el-select v-model="form.contentType">
            <el-option value="JSON" label="JSON" />
            <el-option value="FORM" label="FORM" />
          </el-select>
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.methodDesc" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSubmit">确认</el-button>
      </template>
    </el-dialog>

    <!-- 接口测试对话框 -->
    <el-dialog v-model="testVisible" :title="`接口测试 — ${testApi?.methodName || ''}`" width="700px" destroy-on-close>
      <el-row :gutter="16">
        <el-col :span="14">
          <div class="debug-section">
            <div class="section-title">请求参数</div>
            <el-form label-width="80px" size="small">
              <el-form-item :label="p.paramCode" v-for="p in testInputParams" :key="p.paramCode">
                <el-input v-model="testParams[p.paramCode]" :placeholder="`${p.paramName || ''}`" />
              </el-form-item>
              <el-form-item v-if="testInputParams.length === 0" label="—">
                <span style="color:#999">该接口无入参定义</span>
              </el-form-item>
            </el-form>
          </div>
        </el-col>
        <el-col :span="10">
          <div class="debug-section">
            <div class="section-title">Headers</div>
            <el-form label-width="80px" size="small">
              <el-form-item :label="h.paramCode" v-for="h in testHeaderParams" :key="h.paramCode">
                <el-input v-model="testHeaders[h.paramCode]" :placeholder="`${h.paramName || ''}`" />
              </el-form-item>
              <el-form-item v-if="testHeaderParams.length === 0" label="—">
                <span style="color:#999">无 Header 定义</span>
              </el-form-item>
            </el-form>
          </div>
        </el-col>
      </el-row>
      <div class="debug-section" v-if="testResult !== null" style="margin-top:16px">
        <div class="section-title">
          响应结果
          <el-tag v-if="testSuccess" type="success" size="small" style="margin-left:8px">成功</el-tag>
          <el-tag v-else type="danger" size="small" style="margin-left:8px">失败</el-tag>
        </div>
        <el-input v-model="testResult" type="textarea" :rows="12" readonly style="font-family:monospace;margin-top:8px" />
      </div>
      <template #footer>
        <el-button @click="testVisible = false">关闭</el-button>
        <el-button type="primary" :loading="testLoading" @click="executeTest">发送请求</el-button>
      </template>
    </el-dialog>

    <!-- 导入对话框 -->
    <el-dialog v-model="importVisible" title="导入接口" width="550px" destroy-on-close>
      <el-form label-width="80px">
        <el-form-item label="JSON文件">
          <el-upload :auto-upload="false" :on-change="onImportFileChange" :limit="1" accept=".json" drag>
            <el-icon><UploadFilled /></el-icon>
            <div>拖拽或点击上传 JSON 文件</div>
          </el-upload>
        </el-form-item>
        <el-form-item v-if="importPreview.length > 0" label="预览">
          <div style="max-height:200px;overflow-y:auto;border:1px solid #eee;border-radius:4px;padding:8px">
            <div v-for="(api, idx) in importPreview" :key="idx" style="padding:4px 0;border-bottom:1px solid #f0f0f0">
              <el-tag size="small">{{ api.requestType }}</el-tag>
              <span style="margin-left:8px">{{ api.methodName }}</span>
              <span style="color:#888;margin-left:8px;font-size:12px">{{ api.url }}</span>
            </div>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="importVisible = false">取消</el-button>
        <el-button type="primary" :disabled="importPreview.length === 0" @click="doImport">确认导入</el-button>
      </template>
    </el-dialog>

    <!-- 批量生成对话框 -->
    <el-dialog v-model="batchGenerateVisible" title="批量生成接口" width="750px" destroy-on-close>
      <el-tabs v-model="genTab">
        <el-tab-pane label="CURL命令" name="curl">
          <el-input v-model="genCurl" type="textarea" :rows="4" placeholder="粘贴 CURL 命令..." />
          <el-button type="primary" size="small" style="margin-top:8px" @click="doGenerate('curl')">解析 CURL</el-button>
        </el-tab-pane>
        <el-tab-pane label="Swagger URL" name="swaggerUrl">
          <el-input v-model="genSwaggerUrl" placeholder="如: https://petstore.swagger.io/v2/swagger.json" />
          <el-button type="primary" size="small" style="margin-top:8px" @click="doGenerate('swaggerUrl')">从 URL 获取</el-button>
        </el-tab-pane>
        <el-tab-pane label="Swagger JSON" name="swaggerJson">
          <el-input v-model="genSwaggerJson" type="textarea" :rows="8" placeholder="粘贴 Swagger/OpenAPI JSON..." />
          <el-button type="primary" size="small" style="margin-top:8px" @click="doGenerate('swaggerJson')">解析 JSON</el-button>
        </el-tab-pane>
        <el-tab-pane label="WSDL URL" name="wsdlUrl">
          <el-input v-model="genWsdlUrl" placeholder="如: http://example.com/service?wsdl" />
          <el-button type="primary" size="small" style="margin-top:8px" @click="doGenerate('wsdlUrl')">从 URL 获取</el-button>
        </el-tab-pane>
        <el-tab-pane label="WSDL 内容" name="wsdlContent">
          <el-input v-model="genWsdlContent" type="textarea" :rows="6" placeholder="粘贴 WSDL XML 内容..." />
          <el-input v-model="genWsdlSchemaUrls" type="textarea" :rows="2" placeholder="附加 Schema 地址（每行一个，如 xsd:include/import 的 schemaLocation）" style="margin-top:6px" />
          <el-button type="primary" size="small" style="margin-top:8px" @click="doGenerate('wsdlContent')">解析 WSDL</el-button>
        </el-tab-pane>
      </el-tabs>
      <!-- 生成结果 -->
      <div v-if="genResults.length > 0" style="margin-top:12px">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px">
          <span style="font-weight:600">解析结果 ({{ genResults.length }} 个接口)</span>
          <el-button size="small" type="success" @click="doBatchCreate">批量创建</el-button>
        </div>
        <div style="max-height:250px;overflow-y:auto;border:1px solid #eee;border-radius:4px;padding:8px">
          <div v-for="(api, idx) in genResults" :key="idx" style="padding:6px 0;border-bottom:1px solid #f0f0f0">
            <el-checkbox v-model="genSelected[idx]" style="margin-right:8px" />
            <el-tag size="small">{{ api.methodType === 'WEBSERVICE' ? 'SOAP' : api.requestType }}</el-tag>
            <span style="margin-left:8px;font-weight:500">{{ api.methodName }}</span>
            <span style="color:#888;margin-left:8px;font-size:12px">{{ api.url }}</span>
          </div>
        </div>
      </div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { UploadFilled } from '@element-plus/icons-vue'
import request from '../../utils/request'

const route = useRoute()
const router = useRouter()
const suiteCode = route.params.suiteCode as string
const loading = ref(false)
const tableData = ref([])
const dialogVisible = ref(false)
const isEdit = ref(false)
const formRef = ref()
const form = reactive({
  id: 0, suiteCode, methodName: '', methodType: 'HTTP', requestType: 'GET', url: '', contentType: 'JSON', methodDesc: '',
  soapVersion: '11', soapMethod: '', soapNamespace: '', soapAction: ''
})
const rules = {
  methodName: [{ required: true, message: '请输入接口名称', trigger: 'blur' }],
  url: [{ required: true, message: '请输入URL', trigger: 'blur' }]
}

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.post('/suite/api/list', { suiteCode })
    tableData.value = res.data
  } finally { loading.value = false }
}

function openAdd() {
  isEdit.value = false
  Object.assign(form, { id: 0, methodName: '', methodType: 'HTTP', requestType: 'GET', url: '', contentType: 'JSON', methodDesc: '', soapVersion: '11', soapMethod: '', soapNamespace: '', soapAction: '' })
  dialogVisible.value = true
}

function openEdit(row: any) {
  isEdit.value = true
  Object.assign(form, {
    id: row.id, methodName: row.methodName, methodType: row.methodType || 'HTTP',
    requestType: row.requestType, url: row.url, contentType: row.contentType, methodDesc: row.methodDesc,
    soapVersion: row.soapVersion || '11', soapMethod: row.soapMethod || '', soapNamespace: row.soapNamespace || '', soapAction: row.soapAction || ''
  })
  dialogVisible.value = true
}

function openDetail(row: any) {
  router.push(`/suite/api/${suiteCode}/${row.id}/detail`)
}

async function handleSubmit() {
  await formRef.value?.validate()
  if (isEdit.value) {
    await request.put('/suite/api/update', form)
    ElMessage.success('修改成功')
  } else {
    await request.post('/suite/api/add', form)
    ElMessage.success('创建成功')
  }
  dialogVisible.value = false
  loadData()
}

async function doDelete(row: any) {
  await ElMessageBox.confirm(`确认删除接口「${row.methodName}」？`, '提示', { type: 'warning' })
  await request.delete(`/suite/api/delete/${row.id}`)
  ElMessage.success('删除成功')
  loadData()
}

// 接口测试
const testVisible = ref(false)
const testLoading = ref(false)
const testApi = ref<any>(null)
const testInputParams = ref<any[]>([])
const testHeaderParams = ref<any[]>([])
const testParams = ref<Record<string, any>>({})
const testHeaders = ref<Record<string, any>>({})
const testResult = ref<string | null>(null)
const testSuccess = ref(false)

async function openTest(row: any) {
  testApi.value = row
  testParams.value = {}
  testHeaders.value = {}
  testResult.value = null
  testSuccess.value = false
  testVisible.value = true
  // 加载该接口的入参和 Header
  const [inputRes, headerRes]: any[] = await Promise.all([
    request.get('/parameter/list', { params: { ownerId: row.id, paramType: 1 } }),
    request.get('/parameter/list', { params: { ownerId: row.id, paramType: 4 } })
  ])
  testInputParams.value = inputRes.data || []
  testHeaderParams.value = headerRes.data || []
  // 初始化参数默认值
  for (const p of testInputParams.value) {
    testParams.value[p.paramCode] = p.defaultValue || ''
  }
  for (const h of testHeaderParams.value) {
    testHeaders.value[h.paramCode] = h.defaultValue || ''
  }
}

async function executeTest() {
  if (!testApi.value) return
  testLoading.value = true
  testResult.value = null
  try {
    const res: any = await request.post('/suite/api/debug', {
      apiId: testApi.value.id,
      headers: testHeaders.value,
      params: testParams.value
    })
    if (res.code === 200) {
      testSuccess.value = true
      testResult.value = typeof res.data === 'string' ? res.data : JSON.stringify(res.data, null, 2)
    } else {
      testSuccess.value = false
      testResult.value = res.message || '请求失败'
    }
  } catch (e: any) {
    testSuccess.value = false
    testResult.value = e?.message || '请求异常'
  } finally {
    testLoading.value = false
  }
}

function methodColor(type: string) {
  const map: Record<string, string> = { GET: 'success', POST: 'primary', PUT: 'warning', DELETE: 'danger' }
  return map[type] || 'info'
}

// ========== 导出/导入 ==========
const selectedIds = ref<number[]>([])
function onSelectionChange(rows: any[]) { selectedIds.value = rows.map(r => r.id) }

async function doExport() {
  const res: any = await request.post('/suite/api/export', { suiteCode, ids: selectedIds.value })
  const json = JSON.stringify(res.data, null, 2)
  const blob = new Blob([json], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url; a.download = `export_${suiteCode}.json`; a.click()
  URL.revokeObjectURL(url)
  ElMessage.success('导出成功')
}

const importVisible = ref(false)
const importPreview = ref<any[]>([])
function onImportFileChange(file: any) {
  const reader = new FileReader()
  reader.onload = (e: any) => {
    try {
      importPreview.value = JSON.parse(e.target.result)
      if (!Array.isArray(importPreview.value)) importPreview.value = [importPreview.value]
    } catch { ElMessage.error('JSON 解析失败') }
  }
  reader.readAsText(file.raw)
}

async function doImport() {
  await request.post('/suite/api/import', { suiteCode, apis: importPreview.value })
  ElMessage.success(`导入 ${importPreview.value.length} 个接口成功`)
  importVisible.value = false
  importPreview.value = []
  loadData()
}

// ========== CURL 生成 ==========
async function copyCurl(row: any) {
  // 加载该接口的入参和 Header
  const [inputRes, headerRes]: any[] = await Promise.all([
    request.get('/parameter/list', { params: { ownerId: row.id, paramType: 1 } }),
    request.get('/parameter/list', { params: { ownerId: row.id, paramType: 4 } })
  ])
  const inputParams = (inputRes.data || []) as any[]
  const headerParams = (headerRes.data || []) as any[]

  let curl = `curl -X ${row.requestType || 'GET'}`

  // 添加 Content-Type
  if (row.methodType === 'WEBSERVICE') {
    curl += ` -H "Content-Type: text/xml; charset=utf-8"`
    if (row.soapAction) curl += ` -H "SOAPAction: \"${row.soapAction}\""`
  } else if (row.contentType === 'FORM') {
    curl += ` -H "Content-Type: application/x-www-form-urlencoded"`
  } else if (row.requestType === 'POST' || row.requestType === 'PUT') {
    curl += ` -H "Content-Type: application/json"`
  }

  // 添加 Header 参数
  for (const h of headerParams) {
    const val = h.defaultValue || h.paramCode
    curl += ` -H "${h.paramCode}: ${val}"`
  }

  // 添加查询参数 (GET/DELETE)
  const queryParams = inputParams.filter((p: any) => p.paramPosition === 'query')
  const bodyParams = inputParams.filter((p: any) => p.paramPosition !== 'query' && p.paramPosition !== 'rawBody')
  const rawBodyParam = inputParams.find((p: any) => p.paramPosition === 'rawBody')

  let url = row.url || ''
  if (queryParams.length > 0 && (row.requestType === 'GET' || row.requestType === 'DELETE')) {
    const qs = queryParams.map((p: any) => `${encodeURIComponent(p.paramCode)}=${encodeURIComponent(p.defaultValue || '')}`).join('&')
    url += (url.includes('?') ? '&' : '?') + qs
  }
  curl += ` "${url}"`

  // 添加 Body 参数 (POST/PUT)
  if (row.methodType !== 'WEBSERVICE' && (row.requestType === 'POST' || row.requestType === 'PUT')) {
    if (rawBodyParam) {
      curl += ` -d '${rawBodyParam.defaultValue || ''}'`
    } else if (bodyParams.length > 0) {
      const body: Record<string, string> = {}
      for (const p of bodyParams) body[p.paramCode] = p.defaultValue || ''
      curl += ` -d '${JSON.stringify(body)}'`
    }
  }

  // WebService SOAP 请求体
  if (row.methodType === 'WEBSERVICE' && inputParams.length > 0) {
    const paramXml = inputParams.map((p: any) => `<${p.paramCode}>${p.defaultValue || ''}</${p.paramCode}>`).join('\n  ')
    const ns = row.soapNamespace || 'http://example.com/'
    const methodName = row.soapMethod || 'Request'
    const soapBody = `<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">\n  <soap:Body>\n    <${methodName} xmlns="${ns}">\n      ${paramXml}\n    </${methodName}>\n  </soap:Body>\n</soap:Envelope>`
    curl += ` -d '${soapBody}'`
  }

  navigator.clipboard?.writeText(curl).then(() => ElMessage.success('cURL 已复制到剪贴板'))
    .catch(() => ElMessage.warning('复制失败，请手动复制'))
}

// ========== 批量生成 ==========
const batchGenerateVisible = ref(false)
const genTab = ref('curl')
const genCurl = ref('')
const genSwaggerUrl = ref('')
const genSwaggerJson = ref('')
const genWsdlUrl = ref('')
const genWsdlContent = ref('')
const genWsdlSchemaUrls = ref('')
const genResults = ref<any[]>([])
const genSelected = ref<boolean[]>([])

async function doGenerate(source: string) {
  genLoading.value = true
  try {
    let res: any
    switch (source) {
      case 'curl':
        res = await request.post('/suite/api/generate/from-curl', { curl: genCurl.value })
        break
      case 'swaggerUrl':
        res = await request.post('/suite/api/generate/from-swagger-url', { url: genSwaggerUrl.value })
        break
      case 'swaggerJson':
        res = await request.post('/suite/api/generate/from-swagger-json', { json: genSwaggerJson.value })
        break
      case 'wsdlUrl':
        res = await request.post('/suite/api/generate/from-wsdl-url', { url: genWsdlUrl.value })
        break
      case 'wsdlContent':
        var schemaUrls = genWsdlSchemaUrls.value.split('\n').map(s => s.trim()).filter(s => s)
        res = await request.post('/suite/api/generate/from-wsdl-content', { wsdlContent: genWsdlContent.value, url: genWsdlUrl.value || undefined, schemaUrls })
        break
      default:
        return
    }
    if (res.code === 200) {
      genResults.value = res.data || []
      genSelected.value = genResults.value.map(() => true)
      ElMessage.success(`解析到 ${genResults.value.length} 个接口`)
    } else {
      ElMessage.error(res.message || '解析失败')
    }
  } catch (e: any) {
    ElMessage.error(e?.message || '请求失败')
  } finally {
    genLoading.value = false
  }
}

async function doBatchCreate() {
  const selected = genResults.value.filter((_: any, i: number) => genSelected.value[i])
  if (selected.length === 0) { ElMessage.warning('请选择要创建的接口'); return }
  await request.post('/suite/api/import', { suiteCode, apis: selected })
  ElMessage.success(`批量创建 ${selected.length} 个接口成功`)
  batchGenerateVisible.value = false
  genResults.value = []
  loadData()
}
const genLoading = ref(false)
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
.debug-section {
  background: #fafafa;
  border: 1px solid #eee;
  border-radius: 6px;
  padding: 12px;
}
.debug-section .section-title {
  font-size: 14px;
  font-weight: 600;
  color: #333;
  margin-bottom: 10px;
}
</style>
