<template>
  <div class="page-container">
    <div class="page-header">
      <div>
        <el-button icon="ArrowLeft" link @click="router.back()">返回</el-button>
        <h2 style="display:inline;margin-left:8px">接口管理 - {{ suiteCode }}</h2>
      </div>
      <el-button type="primary" icon="Plus" @click="openAdd">新建接口</el-button>
    </div>

    <el-card class="table-card">
      <el-table :data="tableData" stripe v-loading="loading" height="100%">
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
        <el-table-column label="操作" width="210" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" link @click="openDetail(row)">详情/参数</el-button>
            <el-button size="small" link @click="openEdit(row)">编辑</el-button>
            <el-button size="small" type="success" link @click="openTest(row)">测试</el-button>
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
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
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
