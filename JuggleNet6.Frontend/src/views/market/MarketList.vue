<template>
  <div class="page-container">
    <div class="page-header">
      <h2>🛒 市场</h2>
      <el-button size="small" @click="loadData">刷新</el-button>
    </div>
    <el-card>
      <el-tabs v-model="activeTab" @tab-change="loadData">
        <el-tab-pane label="接口市场" name="api" />
        <el-tab-pane label="流程市场" name="flow" />
        <el-tab-pane label="模型助手市场" name="assistant" />
        <el-tab-pane label="Skills 市场" name="skill" />
        <el-tab-pane label="报表市场" name="report" />
      </el-tabs>
      <el-input v-model="keyword" placeholder="搜索名称/描述" size="small" style="width:220px;margin-bottom:12px" clearable />
      <el-table :data="filteredList" v-loading="loading">
        <el-table-column prop="itemName" label="名称" width="220" show-overflow-tooltip />
        <el-table-column prop="groupName" label="分组" width="140" />
        <el-table-column prop="description" label="描述" min-width="240" show-overflow-tooltip />
        <el-table-column prop="downloadCount" label="下载" width="80" align="center" />
        <el-table-column label="操作" width="140">
          <template #default="{ row }">
            <el-button size="small" type="primary" link :loading="importingId === row.id" @click="doImport(row)">导入</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div style="margin-top:10px;color:#909399;font-size:12px">
        市场条目发布后平台级共享：点击「导入」将内容复制到本租户（按名称/code 去重，已存在自动跳过）。发布入口在各管理页面（流程/模型助手/Skill/报表的「发布到市场」按钮）。
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'

const loading = ref(false)
const activeTab = ref('api')
const keyword = ref('')
const tableData = ref<any[]>([])
const importingId = ref<number | null>(null)

const filteredList = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return tableData.value
  return tableData.value.filter((r: any) =>
    (r.itemName || '').toLowerCase().includes(kw) || (r.description || '').toLowerCase().includes(kw))
})

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.get('/market/list', { params: { type: activeTab.value } })
    tableData.value = res.data || []
  } finally { loading.value = false }
}

async function doImport(row: any) {
  importingId.value = row.id
  try {
    const res: any = await request.post(`/market/import/${row.id}`)
    ElMessage.success(res.data?.message || '导入成功')
    loadData()
  } catch (e: any) {
    ElMessage.error(e?.message || '导入失败')
  } finally { importingId.value = null }
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
</style>
