<template>
  <div class="page-container">
    <div class="page-header">
      <el-button icon="ArrowLeft" link @click="router.back()">返回</el-button>
      <div class="header-info">
        <h2>接口详情</h2>
        <el-tag :type="methodColor(apiInfo?.requestType)" size="small">{{ apiInfo?.requestType }}</el-tag>
        <span class="api-url">{{ apiInfo?.url }}</span>
      </div>
    </div>

    <el-card v-loading="loading">
      <el-descriptions title="基本信息" :column="2" border size="small" style="margin-bottom:24px">
        <el-descriptions-item label="接口名称">{{ apiInfo?.methodName }}</el-descriptions-item>
        <el-descriptions-item label="接口Code">{{ apiInfo?.methodCode }}</el-descriptions-item>
        <el-descriptions-item label="接口类型">
          <el-tag :type="apiInfo?.methodType === 'WEBSERVICE' ? 'warning' : 'info'" size="small">
            {{ apiInfo?.methodType === 'WEBSERVICE' ? 'WebService（SOAP）' : 'API 接口（HTTP）' }}
          </el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="请求方式">
          <el-tag v-if="apiInfo?.methodType !== 'WEBSERVICE'" :type="methodColor(apiInfo?.requestType)" size="small">{{ apiInfo?.requestType }}</el-tag>
          <el-tag v-else type="warning" size="small">SOAP {{ apiInfo?.soapVersion === '12' ? '1.2' : '1.1' }}</el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="内容类型">{{ apiInfo?.methodType === 'WEBSERVICE' ? (apiInfo?.soapVersion === '12' ? 'application/soap+xml' : 'text/xml') : apiInfo?.contentType }}</el-descriptions-item>
        <template v-if="apiInfo?.methodType === 'WEBSERVICE'">
          <el-descriptions-item label="SOAP 操作">{{ apiInfo?.soapMethod || '-' }}</el-descriptions-item>
          <el-descriptions-item label="命名空间">{{ apiInfo?.soapNamespace || '-' }}</el-descriptions-item>
          <el-descriptions-item label="SOAPAction">{{ apiInfo?.soapAction || '-' }}</el-descriptions-item>
        </template>
        <el-descriptions-item label="URL" :span="2">{{ apiInfo?.url }}</el-descriptions-item>
        <el-descriptions-item label="描述" :span="2">{{ apiInfo?.methodDesc || '-' }}</el-descriptions-item>
        <el-descriptions-item label="Mock状态">
          <el-tag :type="apiInfo?.mockJson ? 'success' : 'info'" size="small">
            {{ apiInfo?.mockJson ? '✅ 已启用 Mock' : '未配置 Mock' }}
          </el-tag>
        </el-descriptions-item>
      </el-descriptions>

      <!-- 入参管理 -->
      <div class="param-section">
        <div class="section-header">
          <span class="section-title">
            <el-icon color="#1890ff"><Upload /></el-icon> 入参配置
          </span>
          <div style="display:flex;gap:8px">
            <el-button size="small" icon="Plus" @click="addParam('input')">添加入参</el-button>
            <el-button size="small" icon="CollectionTag" @click="openObjectDialog('input')">来自对象</el-button>
          </div>
        </div>
        <el-table :data="inputParams" border size="small" empty-text="暂无入参">
          <el-table-column type="index" width="50" label="#" />
          <el-table-column label="参数Code" width="160">
            <template #default="{ row }">
              <el-input v-model="row.paramCode" size="small" placeholder="如: city" />
            </template>
          </el-table-column>
          <el-table-column label="参数名" width="130">
            <template #default="{ row }">
              <el-input v-model="row.paramName" size="small" placeholder="如: 城市名" />
            </template>
          </el-table-column>
          <el-table-column label="数据类型" width="110">
            <template #default="{ row }">
              <el-select v-model="row.dataType" size="small" style="width:100%">
                <el-option value="string" label="string" />
                <el-option value="integer" label="integer" />
                <el-option value="double" label="double" />
                <el-option value="boolean" label="boolean" />
                <el-option value="date" label="date（日期）" />
                <el-option value="json" label="json（JSON对象）" />
                <el-option value="object" label="object（对象类型）" />
                <el-option value="array" label="array（对象数组）" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="必填" width="55" align="center">
            <template #default="{ row }">
              <el-checkbox v-model="row.required" :true-value="1" :false-value="0" />
            </template>
          </el-table-column>
          <el-table-column label="位置" width="85">
            <template #default="{ row }">
              <el-select v-model="row.paramPosition" size="small" style="width:100%" clearable>
                <el-option value="" label="自动" />
                <el-option value="query" label="Query" />
                <el-option value="body" label="Body" />
                <el-option value="rawBody" label="RawBody" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column v-if="hasObjInput" label="关联对象" width="120">
            <template #default="{ row }">
              <el-select v-if="row.dataType === 'object' || row.dataType === 'array'" v-model="row.objectCode" size="small" style="width:100%" clearable>
                <el-option v-for="obj in objectList" :key="obj.id" :label="obj.objectName" :value="obj.objectCode" />
              </el-select>
              <span v-else style="color:#ccc;font-size:11px">—</span>
            </template>
          </el-table-column>
          <el-table-column label="默认值" width="100">
            <template #default="{ row }">
              <el-input v-model="row.defaultValue" size="small" placeholder="可选" />
            </template>
          </el-table-column>
          <el-table-column label="描述">
            <template #default="{ row }">
              <el-input v-model="row.description" size="small" placeholder="可选" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="50" align="center">
            <template #default="{ $index }">
              <el-button size="small" type="danger" link @click="inputParams.splice($index, 1)">删</el-button>
            </template>
          </el-table-column>
        </el-table>
        <div style="margin-top:8px;text-align:right">
          <el-button type="primary" size="small" @click="saveParams('input')">保存入参</el-button>
        </div>
      </div>

      <!-- 出参管理 -->
      <div class="param-section" style="margin-top:24px">
        <div class="section-header">
          <span class="section-title">
            <el-icon color="#52c41a"><Download /></el-icon> 出参配置
          </span>
          <div style="display:flex;gap:8px">
            <el-button size="small" type="success" icon="Plus" @click="addParam('output')">添加出参</el-button>
            <el-button size="small" icon="CollectionTag" @click="openObjectDialog('output')">来自对象</el-button>
          </div>
        </div>
        <el-table :data="outputParams" border size="small" empty-text="暂无出参">
          <el-table-column type="index" width="50" label="#" />
          <el-table-column label="参数Code" width="160">
            <template #default="{ row }">
              <el-input v-model="row.paramCode" size="small" placeholder="如: temperature" />
            </template>
          </el-table-column>
          <el-table-column label="参数名" width="130">
            <template #default="{ row }">
              <el-input v-model="row.paramName" size="small" placeholder="如: 温度" />
            </template>
          </el-table-column>
          <el-table-column label="数据类型" width="110">
            <template #default="{ row }">
              <el-select v-model="row.dataType" size="small" style="width:100%">
                <el-option value="string" label="string" />
                <el-option value="integer" label="integer" />
                <el-option value="double" label="double" />
                <el-option value="boolean" label="boolean" />
                <el-option value="date" label="date（日期）" />
                <el-option value="json" label="json（JSON对象）" />
                <el-option value="object" label="object（对象类型）" />
                <el-option value="array" label="array（对象数组）" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="JSON路径" width="140">
            <template #default="{ row }">
              <el-input v-model="row.objectCode" size="small" placeholder="如: data.temp" />
            </template>
          </el-table-column>
          <el-table-column v-if="hasObjOutput" label="关联对象" width="120">
            <template #default="{ row }">
              <el-select v-if="row.dataType === 'object' || row.dataType === 'array'" v-model="row.objectCode" size="small" style="width:100%" clearable>
                <el-option v-for="obj in objectList" :key="obj.id" :label="obj.objectName" :value="obj.objectCode" />
              </el-select>
              <span v-else style="color:#ccc;font-size:11px">—</span>
            </template>
          </el-table-column>
          <el-table-column label="描述">
            <template #default="{ row }">
              <el-input v-model="row.description" size="small" placeholder="可选" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="55" align="center">
            <template #default="{ $index }">
              <el-button size="small" type="danger" link @click="outputParams.splice($index, 1)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
        <div style="margin-top:8px;text-align:right">
          <el-button type="success" size="small" @click="saveParams('output')">保存出参</el-button>
        </div>
      </div>

      <!-- HTTP 头部参数 -->
      <div class="param-section" style="margin-top:24px">
        <div class="section-header">
          <span class="section-title">
            <el-icon color="#fa8c16"><Key /></el-icon> Header 配置（可选）
          </span>
          <div style="display:flex;gap:8px">
            <el-button size="small" icon="Plus" @click="addParam('header')">添加 Header</el-button>
            <el-button size="small" icon="CollectionTag" @click="openObjectDialog('header')">来自对象</el-button>
          </div>
        </div>
        <el-table :data="headerParams" border size="small" empty-text="暂无 Header">
          <el-table-column type="index" width="50" label="#" />
          <el-table-column label="Header名" width="200">
            <template #default="{ row }">
              <el-input v-model="row.paramCode" size="small" placeholder="如: Authorization" />
            </template>
          </el-table-column>
          <el-table-column label="参数说明">
            <template #default="{ row }">
              <el-input v-model="row.paramName" size="small" placeholder="如: 认证Token" />
            </template>
          </el-table-column>
          <el-table-column label="必填" width="70" align="center">
            <template #default="{ row }">
              <el-checkbox v-model="row.required" :true-value="1" :false-value="0" />
            </template>
          </el-table-column>
          <el-table-column label="默认值" width="120">
            <template #default="{ row }">
              <el-input v-model="row.defaultValue" size="small" placeholder="可选" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="70" align="center">
            <template #default="{ $index }">
              <el-button size="small" type="danger" link @click="headerParams.splice($index, 1)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
        <div style="margin-top:8px;text-align:right">
          <el-button size="small" @click="saveParams('header')">保存 Header</el-button>
        </div>
      </div>

      <!-- Mock 数据配置 -->
      <div class="param-section" style="margin-top:24px">
        <div class="section-header">
          <span class="section-title">
            <el-icon color="#722ed1"><MagicStick /></el-icon> Mock 数据配置（可选）
          </span>
          <el-switch v-model="mockEnabled" active-text="启用" inactive-text="关闭" @change="onMockToggle" />
        </div>
        <el-alert
          v-if="mockEnabled"
          title="启用 Mock 后，调试和流程调用将跳过真实请求，直接返回以下预设 JSON 数据"
          type="warning" :closable="false" style="margin-bottom:10px"
        />
        <el-input
          v-if="mockEnabled"
          v-model="mockJson"
          type="textarea"
          :rows="8"
          placeholder='请输入预设的 JSON 响应数据，例如：&#10;{&#10;  "code": 200,&#10;  "message": "success",&#10;  "data": { "name": "mock数据" }&#10;}'
          style="font-family: monospace"
        />
        <div v-if="mockEnabled" style="margin-top:8px;text-align:right">
          <el-button type="warning" size="small" @click="saveMockData">保存 Mock 数据</el-button>
          <el-button size="small" @click="formatMockJson">格式化 JSON</el-button>
        </div>
      </div>
      <!-- 来自对象对话框 -->
      <el-dialog v-model="objectDialogVisible" :title="`选择对象 — 导入${objectDialogTypeLabel}参数`" width="480px">
        <el-form label-width="80px">
          <el-form-item label="选择对象">
            <el-select v-model="selectedObjectId" placeholder="请选择对象类型" style="width:100%" @change="onObjectSelect">
              <el-option v-for="obj in objectList" :key="obj.id" :label="`${obj.objectName} (${obj.objectCode})`" :value="obj.id" />
            </el-select>
          </el-form-item>
        </el-form>
        <el-divider v-if="previewParams.length > 0" />
        <el-table v-if="previewParams.length > 0" :data="previewParams" border size="small" max-height="300">
          <el-table-column prop="paramCode" label="参数Code" />
          <el-table-column prop="paramName" label="参数名" />
          <el-table-column label="数据类型" width="100">
            <template #default="{ row }">
              <el-tag size="small">{{ row.dataType }}</el-tag>
            </template>
          </el-table-column>
        </el-table>
        <template #footer>
          <el-button @click="objectDialogVisible = false">取消</el-button>
          <el-button type="primary" :disabled="previewParams.length === 0" @click="importObjectParams">导入到参数列表</el-button>
        </template>
      </el-dialog>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'

const route = useRoute()
const router = useRouter()
const suiteCode = route.params.suiteCode as string
const apiId = Number(route.params.apiId)

const loading = ref(false)
const apiInfo = ref<any>(null)
const inputParams = ref<any[]>([])
const outputParams = ref<any[]>([])
const headerParams = ref<any[]>([])
const mockEnabled = ref(false)
const mockJson = ref('')

// 检测是否有参数使用了 object/array 类型
const hasObjInput = computed(() => inputParams.value.some(p => p.dataType === 'object' || p.dataType === 'array'))
const hasObjOutput = computed(() => outputParams.value.some(p => p.dataType === 'object' || p.dataType === 'array'))

// 来自对象功能
const objectDialogVisible = ref(false)
const objectDialogType = ref<'input' | 'output' | 'header'>('input')
const objectList = ref<any[]>([])
const selectedObjectId = ref<number | null>(null)
const previewParams = ref<any[]>([])

// paramType: 1=入参, 2=出参, 3=对象属性, 4=header
const PARAM_TYPE = { input: 1, output: 2, header: 4 }

onMounted(async () => {
  loading.value = true
  try {
    // 加载接口基本信息
    const apisRes: any = await request.post('/suite/api/list', { suiteCode })
    const apis = apisRes.data || []
    apiInfo.value = apis.find((a: any) => a.id === apiId) || null

    // 加载各类参数
    await Promise.all([
      loadParams('input'), loadParams('output'), loadParams('header'),
      loadObjects()
    ])

    // 加载 Mock 数据
    if (apiInfo.value?.mockJson) {
      mockEnabled.value = true
      try {
        mockJson.value = JSON.stringify(JSON.parse(apiInfo.value.mockJson), null, 2)
      } catch {
        mockJson.value = apiInfo.value.mockJson
      }
    }
  } finally {
    loading.value = false
  }
})

async function loadParams(type: 'input' | 'output' | 'header') {
  const paramType = PARAM_TYPE[type]
  const res: any = await request.get('/parameter/list', { params: { ownerId: apiId, paramType } })
  const list = (res.data || []).map((p: any) => ({ ...p }))
  if (type === 'input') inputParams.value = list
  else if (type === 'output') outputParams.value = list
  else headerParams.value = list
}

async function loadObjects() {
  try {
    const res: any = await request.get('/object/list')
    objectList.value = res.data || []
  } catch {}
}

function addParam(type: 'input' | 'output' | 'header') {
  const param = {
    paramCode: '', paramName: '', dataType: 'string',
    paramPosition: '', objectCode: '', required: 0, defaultValue: '', description: ''
  }
  if (type === 'input') inputParams.value.push(param)
  else if (type === 'output') outputParams.value.push(param)
  else headerParams.value.push(param)
}

async function saveParams(type: 'input' | 'output' | 'header') {
  if (!apiInfo.value) return
  const paramType = PARAM_TYPE[type]
  const params = type === 'input' ? inputParams.value : type === 'output' ? outputParams.value : headerParams.value

  // 验证 paramCode 不为空
  const invalid = params.find(p => !p.paramCode?.trim())
  if (invalid) { ElMessage.warning('参数Code不能为空'); return }

  await request.post('/parameter/save', {
    ownerId: apiId,
    ownerCode: apiInfo.value.methodCode,
    paramType,
    parameters: params
  })
  ElMessage.success(`${type === 'input' ? '入参' : type === 'output' ? '出参' : 'Header'} 保存成功`)
  await loadParams(type)
}

const objectDialogTypeLabel = { input: '入参', output: '出参', header: 'Header' } as const

async function openObjectDialog(type: 'input' | 'output' | 'header') {
  objectDialogType.value = type
  selectedObjectId.value = null
  previewParams.value = []
  objectDialogVisible.value = true
  // 加载对象列表
  const res: any = await request.get('/object/list')
  objectList.value = res.data || []
}

async function onObjectSelect(objectId: number) {
  const res: any = await request.get('/parameter/list', { params: { ownerId: objectId, paramType: 3 } })
  previewParams.value = (res.data || []).map((p: any) => ({
    ...p,
    // 确保默认值适配参数表格
    required: p.required ?? 0,
    defaultValue: p.defaultValue ?? '',
    description: p.description ?? '',
    objectCode: p.objectCode ?? ''
  }))
}

function importObjectParams() {
  const target = objectDialogType.value === 'input' ? inputParams
    : objectDialogType.value === 'output' ? outputParams
    : headerParams
  for (const p of previewParams.value) {
    target.value.push({
      paramCode: p.paramCode,
      paramName: p.paramName,
      dataType: p.dataType || 'string',
      required: p.required ?? 0,
      defaultValue: p.defaultValue ?? '',
      description: p.description ?? '',
      objectCode: p.objectCode ?? ''
    })
  }
  objectDialogVisible.value = false
  ElMessage.success(`已导入 ${previewParams.value.length} 个参数，请手动保存`)
}

function methodColor(type: string) {
  const map: Record<string, string> = { GET: 'success', POST: 'primary', PUT: 'warning', DELETE: 'danger' }
  return map[type] || 'info'
}

function onMockToggle(enabled: boolean) {
  if (!enabled) {
    mockJson.value = ''
  }
}

async function saveMockData() {
  if (!apiInfo.value) return
  const json = mockEnabled.value ? mockJson.value : ''
  if (mockEnabled.value && json.trim()) {
    try {
      JSON.parse(json)
    } catch {
      ElMessage.error('Mock 数据必须是合法的 JSON 格式')
      return
    }
  }
  await request.put('/suite/api/update', {
    id: apiId,
    suiteCode: apiInfo.value.suiteCode,
    methodCode: apiInfo.value.methodCode,
    methodName: apiInfo.value.methodName,
    methodDesc: apiInfo.value.methodDesc,
    url: apiInfo.value.url,
    requestType: apiInfo.value.requestType,
    contentType: apiInfo.value.contentType,
    methodType: apiInfo.value.methodType,
    mockJson: json
  })
  apiInfo.value.mockJson = json
  ElMessage.success(mockEnabled.value ? 'Mock 数据已保存' : 'Mock 已关闭')
}

function formatMockJson() {
  try {
    const parsed = JSON.parse(mockJson.value)
    mockJson.value = JSON.stringify(parsed, null, 2)
    ElMessage.success('格式化成功')
  } catch {
    ElMessage.error('JSON 格式错误，无法格式化')
  }
}
</script>

<style scoped>
.page-container { padding: 20px; }
.page-header {
  display: flex; align-items: center; gap: 12px;
  margin-bottom: 16px;
}
.header-info { display: flex; align-items: center; gap: 10px; flex: 1; }
.header-info h2 { font-size: 18px; color: #333; margin: 0; }
.api-url { font-size: 13px; color: #888; font-family: monospace; }

.param-section { }
.section-header {
  display: flex; justify-content: space-between; align-items: center;
  margin-bottom: 10px;
}
.section-title {
  font-size: 14px; font-weight: 600; color: #333;
  display: flex; align-items: center; gap: 6px;
}
</style>
