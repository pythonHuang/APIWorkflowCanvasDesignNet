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

    <el-dialog v-model="dialogVisible" :title="form.id ? '编辑供应商' : '添加供应商'" width="560px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="供应商名称"><el-input v-model="form.providerName" placeholder="如 DeepSeek / 通义千问" /></el-form-item>
        <el-form-item label="接口地址"><el-input v-model="form.baseUrl" placeholder="https://api.deepseek.com/v1" /></el-form-item>
        <el-form-item label="API Key"><el-input v-model="form.apiKey" type="password" show-password placeholder="sk-..." /></el-form-item>
        <el-form-item label="默认模型"><el-input v-model="form.model" placeholder="deepseek-chat" /></el-form-item>
        <el-form-item label="可用模型"><el-input v-model="form.models" placeholder="逗号分隔，如 deepseek-chat,deepseek-reasoner" /></el-form-item>
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
  dialogVisible.value = true
}
function openEdit(row: any) {
  form.value = { ...row, enabled: row.enabled === 1 }
  dialogVisible.value = true
}
async function doSave() {
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
