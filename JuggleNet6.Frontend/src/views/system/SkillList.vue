<template>
  <div class="page-container">
    <div class="page-header">
      <h2>🧰 Skill 管理</h2>
      <div style="display:flex;gap:8px">
        <el-button size="small" @click="openAdd">添加技能</el-button>
        <el-upload :show-file-list="false" :auto-upload="false" accept=".json,.md,.markdown" multiple :on-change="onImportFiles" style="display:inline-block">
          <el-button size="small">批量导入</el-button>
        </el-upload>
        <el-button size="small" @click="openSingleImport">单个导入</el-button>
        <el-button size="small" :disabled="!selectedIds.length" @click="batchExport">批量导出 ({{ selectedIds.length }})</el-button>
      </div>
    </div>
    <el-card>
      <div style="display:flex;gap:12px;margin-bottom:12px">
        <el-input v-model="keyword" placeholder="搜索技能名称/描述" size="small" style="width:200px" clearable />
        <el-select v-model="groupFilter" placeholder="分组" size="small" style="width:150px" clearable @change="loadData">
          <el-option v-for="g in groups" :key="g" :label="g" :value="g" />
        </el-select>
      </div>
      <el-table :data="filteredList" v-loading="loading" @selection-change="(rows: any[]) => selectedIds = rows.map((r: any) => r.id)">
        <el-table-column type="selection" width="44" />
        <el-table-column prop="skillName" label="技能名称" width="180" show-overflow-tooltip />
        <el-table-column prop="groupName" label="分组" width="120" />
        <el-table-column prop="description" label="描述" min-width="200" show-overflow-tooltip />
        <el-table-column label="状态" width="90" align="center">
          <template #default="{ row }">
            <el-switch :model-value="row.enabled === 1" @change="(v: any) => toggleEnabled(row, v)" size="small"
              active-text="启用" inactive-text="禁用" inline-prompt style="--el-switch-on-color:#67c23a" />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="200">
          <template #default="{ row }">
            <el-button size="small" link @click="openEdit(row)">编辑</el-button>
            <el-button size="small" link @click="viewSkill(row)">查看</el-button>
            <el-button size="small" type="info" link @click="openPublish(row)">发布</el-button>
            <el-button size="small" type="primary" link @click="doExport(row)">导出</el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div style="margin-top:10px;color:#909399;font-size:12px">
        技能是可复用的提示词/工具定义：大模型节点可勾选多个技能，执行时技能内容自动拼入系统提示词。支持单个/批量导入导出（JSON 或 markdown 格式，markdown 首个 # 标题作为技能名）。
      </div>
    </el-card>

    <!-- 编辑弹窗 -->
    <el-dialog v-model="dialogVisible" :title="form.id ? '编辑技能' : '添加技能'" width="680px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="技能名称"><el-input v-model="form.skillName" placeholder="如 邮件撰写" /></el-form-item>
        <el-form-item label="分组">
          <el-select v-model="form.groupName" filterable allow-create default-first-option placeholder="选择或输入分组" style="width:100%">
            <el-option v-for="g in groups" :key="g" :label="g" :value="g" />
          </el-select>
        </el-form-item>
        <el-form-item label="描述"><el-input v-model="form.description" placeholder="一句话说明技能用途（模型选择依据）" /></el-form-item>
        <el-form-item label="技能内容">
          <el-input v-model="form.content" type="textarea" :rows="10" placeholder="提示词/markdown 内容，执行时拼入系统提示词" />
        </el-form-item>
        <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible=false">取消</el-button>
        <el-button type="primary" @click="doSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 查看/单个导入弹窗 -->
    <el-dialog v-model="viewVisible" :title="viewMode === 'view' ? '技能详情' : '导入技能'" width="680px">
      <template v-if="viewMode === 'view'">
        <div style="font-weight:600;margin-bottom:8px">{{ viewSkillData.skillName }} <span style="color:#999;font-weight:normal">（{{ viewSkillData.groupName }}）</span></div>
        <div style="color:#666;font-size:13px;margin-bottom:8px">{{ viewSkillData.description }}</div>
        <pre class="skill-content">{{ viewSkillData.content }}</pre>
      </template>
      <template v-else>
        <div style="color:#909399;font-size:12px;margin-bottom:8px">粘贴 JSON（skillName/groupName/description/content）或 markdown 内容（首个 # 标题作为技能名）</div>
        <el-input v-model="importText" type="textarea" :rows="12" placeholder="粘贴内容..." />
      </template>
      <template #footer>
        <el-button @click="viewVisible=false">关闭</el-button>
        <el-button v-if="viewMode === 'import'" type="primary" @click="doImport">导入</el-button>
      </template>
    </el-dialog>

    <!-- 发布到市场 -->
    <MarketPublishDialog v-model:visible="publishVisible" item-type="skill"
      :default-name="publishForm.itemName" :default-desc="publishForm.description" :default-group="publishForm.groupName"
      :content-json="publishForm.contentJson" />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import request from '../../utils/request'
import { saveAs } from 'file-saver'
import MarketPublishDialog from '../../components/MarketPublishDialog.vue'

// 发布到市场
const publishVisible = ref(false)
const publishForm = ref<any>({ itemName: '', description: '', groupName: '', contentJson: '{}' })
function openPublish(row: any) {
  publishForm.value = {
    itemName: row.skillName || '',
    description: row.description || '',
    groupName: row.groupName || '',
    contentJson: JSON.stringify({ skillName: row.skillName, groupName: row.groupName, description: row.description, content: row.content })
  }
  publishVisible.value = true
}

const loading = ref(false)
const tableData = ref<any[]>([])
const keyword = ref('')
const groupFilter = ref('')
const selectedIds = ref<number[]>([])
const dialogVisible = ref(false)
const viewVisible = ref(false)
const viewMode = ref<'view' | 'import'>('view')
const viewSkillData = ref<any>({})
const importText = ref('')
const form = ref<any>({ id: 0, skillName: '', groupName: '', description: '', content: '', enabled: true })

const groups = computed(() => [...new Set(tableData.value.map((r: any) => r.groupName).filter(Boolean))] as string[])
const filteredList = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return tableData.value
  return tableData.value.filter((r: any) =>
    (r.skillName || '').toLowerCase().includes(kw) || (r.description || '').toLowerCase().includes(kw))
})

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.get('/skill/list', { params: { group: groupFilter.value || undefined } })
    tableData.value = res.data || []
  } finally { loading.value = false }
}

function openAdd() {
  form.value = { id: 0, skillName: '', groupName: '', description: '', content: '', enabled: true }
  dialogVisible.value = true
}
function openEdit(row: any) {
  form.value = { ...row, enabled: row.enabled === 1 }
  dialogVisible.value = true
}
async function doSave() {
  await request.post('/skill/save', form.value)
  ElMessage.success('保存成功')
  dialogVisible.value = false
  loadData()
}
function viewSkill(row: any) {
  viewMode.value = 'view'
  viewSkillData.value = row
  viewVisible.value = true
}
function openSingleImport() {
  viewMode.value = 'import'
  importText.value = ''
  viewVisible.value = true
}
async function doImport() {
  if (!importText.value.trim()) { ElMessage.warning('请粘贴技能内容'); return }
  await request.post('/skill/import', { content: importText.value })
  ElMessage.success('导入成功')
  viewVisible.value = false
  loadData()
}

/** 批量导入：读取多个文件内容 */
function onImportFiles(files: any) {
  const items = (Array.isArray(files) ? files : [files]).filter((f: any) => f?.raw)
  const reads = items.map((f: any) => new Promise<string>((resolve) => {
    const reader = new FileReader()
    reader.onload = () => resolve(JSON.stringify({ fileName: f.name, content: reader.result as string }))
    reader.readAsText(f.raw)
  }))
  Promise.all(reads).then(async (list) => {
    try {
      const res: any = await request.post('/skill/batch-import', list.map((s) => JSON.parse(s)))
      ElMessage.success(`导入成功 ${res.data?.count ?? list.length} 个技能`)
      loadData()
    } catch (e: any) {
      ElMessage.error(e?.message || '导入失败')
    }
  })
}

async function doExport(row: any) {
  const res: any = await request.get(`/skill/export/${row.id}`)
  const blob = new Blob([JSON.stringify(res.data, null, 2)], { type: 'application/json' })
  saveAs(blob, `${row.skillName || 'skill'}.json`)
}

async function batchExport() {
  if (!selectedIds.value.length) return
  const res: any = await request.get('/skill/batch-export', { params: { ids: selectedIds.value.join(',') } })
  const blob = new Blob([JSON.stringify(res.data || [], null, 2)], { type: 'application/json' })
  saveAs(blob, `skills_${Date.now()}.json`)
}

async function toggleEnabled(row: any, v: boolean) {
  await request.post('/skill/toggle', { id: row.id, enabled: v })
  row.enabled = v ? 1 : 0
  ElMessage.success(v ? '已启用' : '已禁用')
}
async function doDelete(row: any) {
  await ElMessageBox.confirm(`确认删除技能「${row.skillName}」？`, '提示', { type: 'warning' })
  await request.delete(`/skill/delete/${row.id}`)
  ElMessage.success('已删除')
  loadData()
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
.skill-content { background:#f6f8fa;border:1px solid #e4e7ed;border-radius:6px;padding:10px;font-size:12px;line-height:1.7;white-space:pre-wrap;word-break:break-all;margin:0;max-height:400px;overflow:auto }
</style>
