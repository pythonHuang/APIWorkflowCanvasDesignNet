<template>
  <div class="page-container">
    <div class="page-header">
      <h2>大模型设置</h2>
      <el-button type="primary" size="small" @click="openAdd">添加供应商</el-button>
    </div>
    <el-card>
      <el-table :data="tableData" v-loading="loading">
        <el-table-column prop="providerName" label="供应商" width="140" />
        <el-table-column prop="baseUrl" label="接口地址" min-width="220" show-overflow-tooltip />
        <el-table-column prop="models" label="可用模型" min-width="200" show-overflow-tooltip>
          <template #default="{ row }">{{ row.models || row.model || '—' }}</template>
        </el-table-column>
        <el-table-column prop="model" label="默认模型" width="140" show-overflow-tooltip />
        <el-table-column label="状态" width="90" align="center">
          <template #default="{ row }">
            <el-switch :model-value="row.enabled === 1" @change="(v: boolean) => toggleEnabled(row, v)" size="small"
              active-text="启用" inactive-text="禁用" inline-prompt style="--el-switch-on-color:#67c23a" />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="140">
          <template #default="{ row }">
            <el-button size="small" link @click="openEdit(row)">编辑</el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div style="margin-top:10px;color:#909399;font-size:12px">
        支持任意 OpenAI 兼容接口（DeepSeek / 通义千问 / Kimi / OpenAI 等）。接口地址填到 /v1 一级（如 https://api.deepseek.com/v1）；可用模型以逗号分隔（如 deepseek-chat,deepseek-reasoner），模型助手可在对话中切换。
      </div>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="form.id ? '编辑供应商' : '添加供应商'" width="620px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="供应商名称">
          <el-select v-model="form.providerName" filterable allow-create default-first-option placeholder="选择或输入供应商名称"
            style="width:100%" @change="onProviderNameChange">
            <el-option v-for="p in presetProviders" :key="p.name" :label="p.name" :value="p.name" />
          </el-select>
        </el-form-item>
        <el-form-item label="接口地址"><el-input v-model="form.baseUrl" placeholder="https://api.deepseek.com/v1" /></el-form-item>
        <el-form-item label="API Key">
          <div style="display:flex;gap:6px;width:100%">
            <el-input v-model="form.apiKey" type="password" show-password placeholder="sk-..." style="flex:1" />
            <el-button :loading="testingConfig" @click="doFetchModels">获取模型/测试</el-button>
          </div>
          <div v-if="testMsg" :style="`font-size:12px;margin-top:4px;color:${testOk ? '#67c23a' : '#f56c6c'}`">{{ testMsg }}</div>
        </el-form-item>
        <el-form-item label="默认模型">
          <el-select v-model="form.model" filterable allow-create default-first-option placeholder="选择或输入模型名" style="width:100%">
            <el-option v-for="m in fetchedModels" :key="m" :label="m" :value="m" />
          </el-select>
        </el-form-item>
        <el-form-item label="可用模型">
          <el-select v-model="selectedModels" multiple filterable allow-create default-first-option
            collapse-tags collapse-tags-tooltip placeholder="多选可用模型（对话中可切换）" style="width:100%">
            <el-option v-for="m in fetchedModels" :key="m" :label="m" :value="m" />
          </el-select>
        </el-form-item>
        <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
        <el-form-item label="备注"><el-input v-model="form.remark" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible=false">取消</el-button>
        <el-button type="primary" @click="doSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import request from '../../utils/request'

const loading = ref(false)
const tableData = ref<any[]>([])
const dialogVisible = ref(false)
const form = ref<any>({ id: 0, providerName: '', baseUrl: '', apiKey: '', model: '', models: '', enabled: true, remark: '' })
// 模型拉取/多选状态
const fetchedModels = ref<string[]>([])
const selectedModels = ref<string[]>([])
const testingConfig = ref(false)
const testMsg = ref('')
const testOk = ref(false)

// 预置供应商（选择后自动填默认接口地址）
const presetProviders = [
  { name: 'DeepSeek', baseUrl: 'https://api.deepseek.com/v1' },
  { name: '通义千问', baseUrl: 'https://dashscope.aliyuncs.com/compatible-mode/v1' },
  { name: 'Kimi (Moonshot)', baseUrl: 'https://api.moonshot.cn/v1' },
  { name: 'OpenAI', baseUrl: 'https://api.openai.com/v1' },
  { name: '智谱GLM', baseUrl: 'https://open.bigmodel.cn/api/paas/v4' },
  { name: 'Ollama', baseUrl: 'http://localhost:11434/v1' }
]

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.get('/ai/providers')
    tableData.value = res.data || []
  } finally { loading.value = false }
}

function openAdd() {
  form.value = { id: 0, providerName: '', baseUrl: '', apiKey: '', model: '', models: '', enabled: true, remark: '' }
  fetchedModels.value = []
  selectedModels.value = []
  testMsg.value = ''
  dialogVisible.value = true
}
function openEdit(row: any) {
  form.value = { ...row, enabled: row.enabled === 1 }
  fetchedModels.value = String(row.models || '').split(',').map((s: string) => s.trim()).filter(Boolean)
  selectedModels.value = [...fetchedModels.value]
  testMsg.value = ''
  dialogVisible.value = true
}

/** 选择预置供应商 → 自动填接口地址默认值 */
function onProviderNameChange() {
  const preset = presetProviders.find((p: any) => p.name === form.value.providerName)
  if (preset && !form.value.baseUrl) form.value.baseUrl = preset.baseUrl
}

/** 获取模型列表（兼作配置测试） */
async function doFetchModels() {
  if (!form.value.baseUrl) { ElMessage.warning('请先填写接口地址'); return }
  testingConfig.value = true
  testMsg.value = ''
  try {
    const res: any = await request.post('/ai/fetch-models', { baseUrl: form.value.baseUrl, apiKey: form.value.apiKey })
    const models: string[] = res.data?.models || []
    fetchedModels.value = models
    // 默认模型：优先保留当前选择，否则取第一个
    if (models.length > 0) {
      if (!form.value.model || !models.includes(form.value.model)) form.value.model = models[0]
      selectedModels.value = models.filter((m: string) => selectedModels.value.includes(m))
      if (selectedModels.value.length === 0) selectedModels.value = [...models]
    }
    testOk.value = true
    testMsg.value = `✅ 配置测试通过，共 ${models.length} 个模型`
  } catch (e: any) {
    testOk.value = false
    testMsg.value = e?.message || '测试失败'
  } finally { testingConfig.value = false }
}

async function doSave() {
  form.value.models = selectedModels.value.join(',')
  await request.post('/ai/provider/save', form.value)
  ElMessage.success('保存成功')
  dialogVisible.value = false
  loadData()
}
async function toggleEnabled(row: any, v: boolean) {
  await request.post('/ai/provider/toggle', { id: row.id, enabled: v })
  row.enabled = v ? 1 : 0
  ElMessage.success(v ? '已启用' : '已禁用')
}
async function doDelete(row: any) {
  await ElMessageBox.confirm(`确认删除供应商「${row.providerName}」？`, '提示', { type: 'warning' })
  await request.delete(`/ai/provider/${row.id}`)
  ElMessage.success('已删除')
  loadData()
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
</style>
