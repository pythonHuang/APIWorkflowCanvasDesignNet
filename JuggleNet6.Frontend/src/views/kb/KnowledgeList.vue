<template>
  <div class="page-container">
    <div class="page-header">
      <h2>📚 知识库管理</h2>
      <el-button type="primary" size="small" @click="openAdd">新建知识库</el-button>
    </div>
    <el-card>
      <el-table :data="tableData" v-loading="loading">
        <el-table-column prop="kbName" label="名称" min-width="150" show-overflow-tooltip>
          <template #default="{ row }">
            <el-button link type="primary" @click="$router.push(`/kb/detail/${row.id}`)">{{ row.kbName }}</el-button>
          </template>
        </el-table-column>
        <el-table-column prop="description" label="描述" min-width="200" show-overflow-tooltip />
        <el-table-column label="切片" width="110">
          <template #default="{ row }">{{ row.chunkSize }}字/重叠{{ row.chunkOverlap }}</template>
        </el-table-column>
        <el-table-column label="检索方式" width="100">
          <template #default="{ row }">
            <el-tag size="small" :type="row.retrieveType === 'vector' ? 'warning' : 'primary'">{{ row.retrieveType === 'vector' ? '向量' : '文本' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="chunkCount" label="片段数" width="80" align="center" />
        <el-table-column label="启用" width="70" align="center">
          <template #default="{ row }"><el-switch :model-value="row.enabled === 1" @change="(v: boolean) => toggleEnabled(row, v)" size="small" /></template>
        </el-table-column>
        <el-table-column label="操作" width="140">
          <template #default="{ row }">
            <el-button size="small" link @click="openEdit(row)">配置</el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="form.id ? '编辑知识库' : '新建知识库'" width="560px">
      <el-form :model="form" label-width="100px">
        <el-form-item label="名称"><el-input v-model="form.kbName" placeholder="如 产品知识库" /></el-form-item>
        <el-form-item label="描述"><el-input v-model="form.description" placeholder="知识库用途说明" /></el-form-item>
        <el-form-item label="切片大小"><el-input-number v-model="form.chunkSize" :min="100" :max="2000" :step="100" style="width:100%" /></el-form-item>
        <el-form-item label="切片重叠"><el-input-number v-model="form.chunkOverlap" :min="0" :max="500" :step="10" style="width:100%" /></el-form-item>
        <el-form-item label="检索方式">
          <el-radio-group v-model="form.retrieveType">
            <el-radio value="text">文本（关键词匹配）</el-radio>
            <el-radio value="vector">向量（语义相似度）</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="form.retrieveType === 'vector'" label="向量模型">
          <el-input v-model="form.vectorModel" placeholder="如 text-embedding-ada-002 / bge-large-zh（空=供应商默认）" />
        </el-form-item>
        <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
      </el-form>
      <div v-if="form.retrieveType === 'vector'" style="color:#909399;font-size:12px;margin:0 0 8px 100px">
        向量检索通过大模型供应商的 embeddings 接口生成向量（系统设置 → 大模型设置中启用），片段入库/重新向量化时计算，检索时余弦相似度排序。
      </div>
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
const form = ref<any>({ id: 0, kbName: '', description: '', chunkSize: 500, chunkOverlap: 50, retrieveType: 'text', vectorModel: '', enabled: true })

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.get('/kb/list')
    tableData.value = res.data || []
  } finally { loading.value = false }
}

function openAdd() {
  form.value = { id: 0, kbName: '', description: '', chunkSize: 500, chunkOverlap: 50, retrieveType: 'text', vectorModel: '', enabled: true }
  dialogVisible.value = true
}
function openEdit(row: any) {
  form.value = { ...row, enabled: row.enabled === 1 }
  dialogVisible.value = true
}
async function doSave() {
  await request.post('/kb/save', { ...form.value, enabled: form.value.enabled ? 1 : 0 })
  ElMessage.success('保存成功')
  dialogVisible.value = false
  loadData()
}
async function toggleEnabled(row: any, v: boolean) {
  await request.post('/kb/save', { ...row, enabled: v })
  row.enabled = v ? 1 : 0
}
async function doDelete(row: any) {
  await ElMessageBox.confirm(`确认删除知识库「${row.kbName}」？`, '提示', { type: 'warning' })
  await request.delete(`/kb/delete/${row.id}`)
  ElMessage.success('已删除')
  loadData()
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
</style>
