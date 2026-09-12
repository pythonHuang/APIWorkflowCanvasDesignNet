<template>
  <div class="page-container">
    <div class="page-header">
      <h2>🛒 市场</h2>
      <div style="display:flex;gap:8px">
        <el-button size="small" type="primary" @click="openDiscover">🔍 发现官方市场</el-button>
        <el-button size="small" @click="loadData">刷新</el-button>
      </div>
    </div>
    <el-card>
      <el-tabs v-model="activeTab" @tab-change="loadData">
        <el-tab-pane label="接口市场" name="api" />
        <el-tab-pane label="流程市场" name="flow" />
        <el-tab-pane label="模型助手市场" name="assistant" />
        <el-tab-pane label="Skills 市场" name="skill" />
        <el-tab-pane label="报表市场" name="report" />
      </el-tabs>
      <div style="display:flex;gap:16px;align-items:center;margin-bottom:12px">
        <el-input v-model="keyword" placeholder="搜索名称/描述" size="small" style="width:220px" clearable />
        <el-checkbox v-model="onlyFavorite" size="small" @change="loadData">⭐ 只看收藏</el-checkbox>
      </div>
      <el-table :data="filteredList" v-loading="loading">
        <el-table-column label="名称" width="240" show-overflow-tooltip>
          <template #default="{ row }"><span style="margin-right:4px">{{ row.icon || '📦' }}</span>{{ row.itemName }}</template>
        </el-table-column>
        <el-table-column prop="groupName" label="分组" width="120" />
        <el-table-column prop="author" label="作者" width="110" show-overflow-tooltip />
        <el-table-column prop="version" label="版本" width="80" align="center" />
        <el-table-column prop="updatedAt" label="更新日期" width="110" />
        <el-table-column prop="description" label="描述" min-width="220" show-overflow-tooltip />
        <el-table-column prop="downloadCount" label="下载" width="70" align="center" />
        <el-table-column label="收藏" width="70" align="center">
          <template #default="{ row }">
            <el-button size="small" link :loading="favoritingId === row.id" @click="toggleFavorite(row)">
              <span :style="`font-size:16px;color:${row.favorited ? '#f7ba2a' : '#c0c4cc'}`">{{ row.favorited ? '★' : '☆' }}</span>
            </el-button>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="140" fixed="right">
          <template #default="{ row }">
            <el-button size="small" type="primary" link :loading="importingId === row.id" @click="doImport(row)">直接应用</el-button>
            <el-button size="small" link @click="doDownload(row)">下载</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div style="margin-top:10px;color:#909399;font-size:12px">
        市场条目发布后平台级共享：点击「直接应用」将内容复制到本租户（按名称/code 去重，已存在自动跳过）；「下载」导出条目 JSON 文件。发布入口在各管理页面（流程/模型助手/Skill/报表的「发布到市场」按钮）。
      </div>
    </el-card>

    <!-- 发现官方市场 -->
    <el-dialog v-model="discoverVisible" title="🔍 发现官方市场（GitHub）" width="820px">
      <div style="margin-bottom:10px;color:#606266;font-size:13px;line-height:1.7">
        从 GitHub 仓库 <b>pythonHuang/APIWorkflowCanvasDesignNet</b> 的 market 目录拉取
        <b>index.json</b> 及各类型 <b>{id}.json</b> 条目文件到本地 market 目录。
        条目以 id 识别：<b>id 相同则更新，否则新增</b>。管理员审核合并后即可在此发现。
      </div>
      <div style="display:flex;gap:8px;margin-bottom:12px">
        <el-button size="small" type="primary" :loading="discovering" @click="doDiscover">🔍 发现（拉取最新）</el-button>
        <el-button size="small" :loading="localLoading" @click="loadLocalFiles">刷新本地列表</el-button>
        <span v-if="discoverMsg" :style="`font-size:12px;color:${discoverOk ? '#67c23a' : '#f56c6c'};line-height:24px`">{{ discoverMsg }}</span>
      </div>
      <el-table :data="localFiles" v-loading="localLoading" max-height="420" size="small">
        <el-table-column label="名称" width="220" show-overflow-tooltip>
          <template #default="{ row }"><span style="margin-right:4px">{{ row.icon || '📦' }}</span>{{ row.name }}</template>
        </el-table-column>
        <el-table-column prop="type" label="类型" width="90" align="center">
          <template #default="{ row }">{{ typeName(row.type) }}</template>
        </el-table-column>
        <el-table-column prop="author" label="作者" width="100" show-overflow-tooltip />
        <el-table-column prop="version" label="版本" width="70" align="center" />
        <el-table-column prop="updatedAt" label="更新日期" width="105" />
        <el-table-column prop="description" label="描述" min-width="160" show-overflow-tooltip />
        <el-table-column label="状态" width="90" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.imported" type="success" size="small">已导入</el-tag>
            <el-tag v-else type="info" size="small">未导入</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="100" align="center">
          <template #default="{ row }">
            <el-button size="small" type="primary" link :loading="importingFileId === `${row.type}_${row.marketId}`"
              @click="doImportFile(row)">{{ row.imported ? '更新' : '导入' }}</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!localLoading && localFiles.length === 0" description="本地 market 目录暂无条目，请先点击「发现」拉取" :image-size="80" />
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'

const loading = ref(false)
const activeTab = ref('api')
const keyword = ref('')
const onlyFavorite = ref(false)
const tableData = ref<any[]>([])
const importingId = ref<number | null>(null)
const favoritingId = ref<number | null>(null)

// ====== 发现官方市场 ======
const discoverVisible = ref(false)
const discovering = ref(false)
const localLoading = ref(false)
const discoverMsg = ref('')
const discoverOk = ref(true)
const localFiles = ref<any[]>([])
const importingFileId = ref('')

const filteredList = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return tableData.value
  return tableData.value.filter((r: any) =>
    (r.itemName || '').toLowerCase().includes(kw) || (r.description || '').toLowerCase().includes(kw))
})

function typeName(type: string) {
  const map: Record<string, string> = { api: '接口', flow: '流程', assistant: '模型助手', skill: 'Skill', report: '报表' }
  return map[type] || type
}

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.get('/market/list', { params: { type: activeTab.value, favorite: onlyFavorite.value ? 1 : 0 } })
    tableData.value = res.data || []
  } finally { loading.value = false }
}

async function toggleFavorite(row: any) {
  favoritingId.value = row.id
  try {
    const res: any = await request.post(row.favorited ? `/market/unfavorite/${row.id}` : `/market/favorite/${row.id}`)
    row.favorited = res.data
    if (onlyFavorite.value && !row.favorited) {
      tableData.value = tableData.value.filter((r: any) => r.id !== row.id)
    }
    ElMessage.success(row.favorited ? '已收藏' : '已取消收藏')
  } catch (e: any) {
    ElMessage.error(e?.message || '操作失败')
  } finally { favoritingId.value = null }
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

/** 下载条目 JSON 文件（浏览器触发下载） */
function doDownload(row: any) {
  const payload = {
    id: row.marketItemId ?? null,
    type: row.itemType,
    name: row.itemName,
    description: row.description,
    icon: row.icon,
    author: row.author,
    version: row.version,
    updatedAt: row.updatedAt,
    content: JSON.parse(row.contentJson || '{}')
  }
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = `${row.itemType}_${row.itemName}.json`
  a.click()
  URL.revokeObjectURL(a.href)
  ElMessage.success('已下载条目 JSON')
}

// ====== 发现 ======
function openDiscover() {
  discoverVisible.value = true
  discoverMsg.value = ''
  loadLocalFiles()
}

async function doDiscover() {
  discovering.value = true
  discoverMsg.value = ''
  try {
    const res: any = await request.get('/market/discover')
    discoverOk.value = true
    discoverMsg.value = `✅ ${res.data?.message || '发现完成'}`
    await loadLocalFiles()
  } catch (e: any) {
    discoverOk.value = false
    discoverMsg.value = e?.message || '发现失败'
  } finally { discovering.value = false }
}

async function loadLocalFiles() {
  localLoading.value = true
  try {
    const res: any = await request.get('/market/local-files')
    localFiles.value = res.data || []
  } catch { localFiles.value = [] } finally { localLoading.value = false }
}

async function doImportFile(row: any) {
  importingFileId.value = `${row.type}_${row.marketId}`
  try {
    const res: any = await request.post('/market/import-file', { type: row.type, marketId: row.marketId })
    ElMessage.success(res.data?.message || '导入成功')
    await loadLocalFiles()
    await loadData()
  } catch (e: any) {
    ElMessage.error(e?.message || '导入失败')
  } finally { importingFileId.value = '' }
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
</style>
