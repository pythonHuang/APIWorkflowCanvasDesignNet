<template>
  <div class="page-container">
    <div class="page-header">
      <h2>🔌 接口智能接入助手</h2>
      <div style="display:flex;gap:8px;align-items:center">
        <span style="font-size:12px;color:#888">模型:</span>
        <el-select v-model="providerId" size="small" style="width:150px" @change="onProviderChange">
          <el-option v-for="p in providers" :key="p.id" :label="p.providerName" :value="p.id" />
        </el-select>
        <el-select v-model="currentModel" size="small" style="width:170px">
          <el-option v-for="m in modelOptions" :key="m" :label="m" :value="m" />
        </el-select>
        <el-button size="small" @click="$router.push('/system/ai-provider')">模型管理</el-button>
      </div>
    </div>

    <el-card style="margin-bottom:12px" shadow="never">
      <el-input v-model="requirement" type="textarea" :rows="5"
        placeholder="描述接入需求，例如：接入用户中心相关接口：根据用户id查询用户信息、修改用户昵称、查询用户收货地址列表" />
      <div style="margin-top:8px;display:flex;gap:8px;align-items:center">
        <el-button type="primary" icon="MagicStick" :loading="generating" @click="doGenerate">生成接口方案</el-button>
        <span style="font-size:12px;color:#909399">AI 将生成套件与接口定义（code/名称/路径/请求方式），确认后写入平台。</span>
      </div>
      <div v-if="error" class="err-box">❌ {{ error }}</div>
    </el-card>

    <!-- 预览 -->
    <el-card v-if="preview" shadow="never">
      <template #header>
        <div style="display:flex;justify-content:space-between;align-items:center">
          <span style="font-weight:600">方案预览（{{ preview.suites.length }} 个套件 / {{ preview.apis.length }} 个接口）</span>
          <el-button type="success" size="small" icon="Check" :loading="applying" @click="doApply">确认接入</el-button>
        </div>
      </template>
      <div style="font-weight:600;margin:0 0 6px;font-size:13px">套件</div>
      <el-table :data="preview.suites" size="small" border style="margin-bottom:12px">
        <el-table-column prop="suiteCode" label="套件code" width="160" />
        <el-table-column prop="suiteName" label="套件名称" min-width="160" />
        <el-table-column prop="suiteDesc" label="描述" min-width="200" show-overflow-tooltip />
      </el-table>
      <div style="font-weight:600;margin:0 0 6px;font-size:13px">接口</div>
      <el-table :data="preview.apis" size="small" border max-height="320" row-key="methodCode">
        <el-table-column type="expand">
          <template #default="{ row }">
            <div style="display:flex;gap:16px;padding:8px 16px">
              <div style="flex:1">
                <div style="font-weight:600;margin-bottom:4px;font-size:12px">入参（{{ row.inputParams?.length || 0 }}）</div>
                <el-table :data="row.inputParams || []" size="small" border>
                  <el-table-column prop="paramCode" label="参数code" min-width="130" show-overflow-tooltip />
                  <el-table-column prop="paramName" label="名称" min-width="100" show-overflow-tooltip />
                  <el-table-column prop="paramType" label="类型" width="90" />
                  <el-table-column prop="paramPosition" label="位置" width="70" />
                  <el-table-column label="必填" width="60" align="center">
                    <template #default="{ row: r }"><el-tag size="small" :type="r.required ? 'danger' : 'info'">{{ r.required ? '是' : '否' }}</el-tag></template>
                  </el-table-column>
                  <el-table-column prop="description" label="说明" min-width="120" show-overflow-tooltip />
                </el-table>
              </div>
              <div style="flex:1">
                <div style="font-weight:600;margin-bottom:4px;font-size:12px">出参（{{ row.outputParams?.length || 0 }}）</div>
                <el-table :data="row.outputParams || []" size="small" border>
                  <el-table-column prop="paramCode" label="参数code" min-width="130" show-overflow-tooltip />
                  <el-table-column prop="paramName" label="名称" min-width="100" show-overflow-tooltip />
                  <el-table-column prop="paramType" label="类型" width="90" />
                  <el-table-column prop="description" label="说明" min-width="120" show-overflow-tooltip />
                </el-table>
              </div>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="suiteCode" label="套件" width="110" />
        <el-table-column prop="methodCode" label="接口code" width="160" />
        <el-table-column prop="methodName" label="名称" min-width="130" />
        <el-table-column prop="methodDesc" label="描述" min-width="150" show-overflow-tooltip />
        <el-table-column prop="requestType" label="方式" width="70" />
        <el-table-column prop="url" label="路径" min-width="140" show-overflow-tooltip />
      </el-table>
      <div v-if="applied" style="margin-top:10px;color:#67c23a;font-size:13px">
        ✅ 已接入：新增 {{ applied.createdSuites }} 个套件、{{ applied.createdApis }} 个接口（已存在的自动跳过）
        <el-button size="small" link type="primary" @click="$router.push('/suite/list')">去套件管理查看</el-button>
      </div>
    </el-card>

    <el-card v-else shadow="never">
      <el-empty description="输入接入需求后点击「生成接口方案」，AI 自动规划套件与接口定义" :image-size="80" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'

const providers = ref<any[]>([])
const providerId = ref<number>(0)
const currentModel = ref('')
const requirement = ref('')
const generating = ref(false)
const applying = ref(false)
const error = ref('')
const preview = ref<any>(null)
const applied = ref<any>(null)

const currentProvider = computed(() => providers.value.find((p: any) => p.id === providerId.value))
const modelOptions = computed(() => {
  const p = currentProvider.value
  const list = String(p?.models || '').split(',').map((s: string) => s.trim()).filter(Boolean)
  if (!list.includes(p?.model)) list.unshift(p?.model || '')
  return list.filter(Boolean)
})

onMounted(async () => {
  try {
    const res: any = await request.get('/ai/providers/enabled')
    providers.value = res.data || []
    if (providers.value.length > 0) {
      providerId.value = providers.value[0].id
      currentModel.value = currentProvider.value?.model || ''
    }
  } catch { /* 未配置供应商 */ }
})

function onProviderChange() {
  currentModel.value = currentProvider.value?.model || ''
}

async function doGenerate() {
  if (!requirement.value.trim()) { ElMessage.warning('请先描述接入需求'); return }
  if (!providerId.value) { ElMessage.warning('请先在系统设置 → 大模型设置中启用供应商'); return }
  generating.value = true
  error.value = ''
  preview.value = null
  applied.value = null
  try {
    const suitesRes: any = await request.get('/suite/list')
    const suites = (suitesRes.data || []).map((s: any) => ({ suiteCode: s.suiteCode, suiteName: s.suiteName }))
    const res: any = await request.post('/ai/generate-apis', {
      requirement: requirement.value, providerId: providerId.value, model: currentModel.value || null, existingSuites: suites
    })
    preview.value = res.data
  } catch (e: any) {
    error.value = e?.message || '生成失败'
  } finally { generating.value = false }
}

async function doApply() {
  applying.value = true
  error.value = ''
  try {
    const res: any = await request.post('/ai/apply-apis', {
      suites: preview.value.suites, apis: preview.value.apis
    })
    applied.value = res.data
    ElMessage.success('接入成功')
  } catch (e: any) {
    error.value = e?.message || '接入失败'
  } finally { applying.value = false }
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
.err-box { margin-top:10px;padding:8px 12px;background:#fef0f0;border:1px solid #fbc4c4;border-radius:6px;color:#f56c6c;font-size:12px;white-space:pre-wrap }
</style>
