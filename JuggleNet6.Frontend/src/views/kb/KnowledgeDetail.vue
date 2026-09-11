<template>
  <div class="page-container">
    <div class="page-header">
      <div>
        <h2>📚 {{ kb?.kbName || '知识库' }}</h2>
        <div style="font-size:12px;color:#888;margin-top:4px">{{ kb?.description || '' }} · 片段 {{ kb?.chunkCount || 0 }} 个 · 检索方式 {{ kb?.retrieveType === 'vector' ? '向量' : '文本' }}</div>
      </div>
      <div style="display:flex;gap:8px">
        <el-button size="small" @click="$router.push('/kb/list')">返回列表</el-button>
        <el-button size="small" type="warning" :loading="cleaning" @click="doClean">清洗</el-button>
        <el-button v-if="kb?.retrieveType === 'vector'" size="small" :loading="revectorizing" @click="doRevectorize">重新向量化</el-button>
      </div>
    </div>

    <el-card style="margin-bottom:12px" shadow="never">
      <el-tabs v-model="tab">
        <el-tab-pane label="文档与片段" name="docs">
          <div style="display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap;align-items:center">
            <el-upload :show-file-list="false" :http-request="doUpload"
              accept=".txt,.md,.markdown,.docx,.xlsx,.pdf">
              <el-button type="primary" size="small" icon="Upload" :loading="uploading">上传文档</el-button>
            </el-upload>
            <span style="font-size:12px;color:#909399">支持 word / excel / pdf / txt / markdown，自动解析并切片入库</span>
          </div>
          <div style="margin-bottom:12px">
            <el-input v-model="manualChunk" type="textarea" :rows="3" placeholder="手动增加片段（如：售后服务政策：七天无理由退货…）" />
            <el-button size="small" style="margin-top:6px" @click="doAddChunk">添加片段</el-button>
          </div>
          <el-table :data="documents" size="small" border>
            <el-table-column prop="docName" label="文档" min-width="200" show-overflow-tooltip />
            <el-table-column prop="docType" label="类型" width="100" />
            <el-table-column prop="createdAt" label="时间" width="160" show-overflow-tooltip />
          </el-table>
          <div style="font-weight:600;margin:12px 0 6px;font-size:13px">片段列表</div>
          <el-table :data="chunks" size="small" border max-height="300">
            <el-table-column prop="seqNo" label="#" width="50" />
            <el-table-column prop="content" label="内容" min-width="300" show-overflow-tooltip />
          </el-table>
        </el-tab-pane>
        <el-tab-pane label="检索测试" name="search">
          <div style="display:flex;gap:8px;margin-bottom:12px">
            <el-input v-model="searchQuery" size="small" style="flex:1" placeholder="输入查询内容测试检索效果" @keydown.enter="doSearch" />
            <el-input-number v-model="searchTopK" size="small" :min="1" :max="20" style="width:100px" />
            <el-button size="small" type="primary" :loading="searching" @click="doSearch">检索</el-button>
          </div>
          <el-card v-for="(r, i) in searchResults" :key="i" shadow="never" style="margin-bottom:8px">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:4px">
              <span style="font-size:12px;color:#909399">#{{ i + 1 }}</span>
              <el-tag size="small" type="success">相似度 {{ r.score }}</el-tag>
            </div>
            <div style="font-size:13px;line-height:1.7">{{ r.content }}</div>
          </el-card>
        </el-tab-pane>
        <el-tab-pane label="匹配记录" name="logs">
          <el-table :data="logs" size="small" border>
            <el-table-column prop="query" label="查询内容" min-width="200" show-overflow-tooltip />
            <el-table-column prop="score" label="最高分" width="90" />
            <el-table-column prop="createdAt" label="时间" width="170" show-overflow-tooltip />
          </el-table>
        </el-tab-pane>
      </el-tabs>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'

const route = useRoute()
const kbId = Number(route.params.id) || 0
const kb = ref<any>(null)
const tab = ref('docs')
const documents = ref<any[]>([])
const chunks = ref<any[]>([])
const manualChunk = ref('')
const uploading = ref(false)
const cleaning = ref(false)
const revectorizing = ref(false)
const searchQuery = ref('')
const searchTopK = ref(5)
const searching = ref(false)
const searchResults = ref<any[]>([])
const logs = ref<any[]>([])

onMounted(loadAll)

async function loadAll() {
  try {
    const res: any = await request.get('/kb/list')
    kb.value = (res.data || []).find((k: any) => k.id === kbId) || null
  } catch { /* 拦截器已提示 */ }
  await loadDocs()
  await loadLogs()
}

async function loadDocs() {
  try {
    const [dRes, cRes]: any[] = await Promise.all([
      request.get(`/kb/documents/${kbId}`),
      request.get(`/kb/chunks/${kbId}`)
    ])
    documents.value = dRes.data || []
    chunks.value = cRes.data || []
  } catch { /* 忽略 */ }
}

async function loadLogs() {
  try {
    const res: any = await request.get(`/kb/match-logs/${kbId}`)
    logs.value = res.data || []
  } catch { logs.value = [] }
}

async function doUpload(opt: any) {
  uploading.value = true
  try {
    const fd = new FormData()
    fd.append('file', opt.file)
    const res: any = await request.post(`/kb/upload/${kbId}`, fd, { headers: { 'Content-Type': 'multipart/form-data' } })
    ElMessage.success(`已解析并切片 ${res.data?.chunkCount || 0} 个片段`)
    await loadAll()
  } catch (e: any) {
    ElMessage.error(e?.message || '上传失败')
  } finally { uploading.value = false }
}

async function doAddChunk() {
  if (!manualChunk.value.trim()) { ElMessage.warning('请输入片段内容'); return }
  await request.post(`/kb/chunk/${kbId}`, { content: manualChunk.value })
  ElMessage.success('片段已添加')
  manualChunk.value = ''
  await loadAll()
}

async function doClean() {
  cleaning.value = true
  try {
    const res: any = await request.post(`/kb/clean/${kbId}`)
    ElMessage.success(`清洗完成：移除 ${res.data?.removed || 0} 个片段`)
    await loadAll()
  } finally { cleaning.value = false }
}

async function doRevectorize() {
  revectorizing.value = true
  try {
    const res: any = await request.post(`/kb/revectorize/${kbId}`)
    ElMessage.success(`重新向量化完成：${res.data?.done || 0}/${res.data?.total || 0}`)
  } catch (e: any) {
    ElMessage.error(e?.message || '向量化失败，请确认已启用大模型供应商')
  } finally { revectorizing.value = false }
}

async function doSearch() {
  if (!searchQuery.value.trim()) { ElMessage.warning('请输入查询内容'); return }
  searching.value = true
  try {
    const res: any = await request.post(`/kb/search/${kbId}`, { query: searchQuery.value, topK: searchTopK.value })
    searchResults.value = res.data || []
  } catch (e: any) {
    ElMessage.error(e?.message || '检索失败')
  } finally { searching.value = false }
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box;overflow:auto }
.page-header { display:flex;justify-content:space-between;align-items:flex-start;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
</style>
