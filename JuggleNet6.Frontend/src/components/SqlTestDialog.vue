<template>
  <el-dialog :model-value="visible" @update:model-value="onVisibleChange" title="🧪 测试 SQL" width="760px" append-to-body>
    <div v-if="params.length>0" style="display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap">
      <div v-for="p in params" :key="p.name" style="display:flex;align-items:center;gap:4px">
        <span style="font-size:12px">{{ p.label || p.name }} (@{{ p.name }}):</span>
        <el-input v-model="values[p.name]" size="small" style="width:140px" />
      </div>
    </div>
    <el-button size="small" type="primary" icon="VideoPlay" :loading="loading" @click="runTest">执行测试</el-button>
    <div v-if="error" class="test-error">❌ {{ error }}</div>
    <div v-if="result" style="margin-top:10px">
      <div style="margin-bottom:6px;color:#67c23a;font-size:13px">
        ✅ 查询成功，共 {{ result.rowCount }} 行{{ result.truncated ? '（仅显示前 100 行）' : '' }}
      </div>
      <el-table v-if="result.rows.length" :data="result.rows" size="small" border max-height="300">
        <el-table-column v-for="c in result.columns" :key="c.name || c" :prop="c.name || c"
          :label="c.comment ? `${c.name} (${c.comment})` : (c.name || c)" min-width="110" show-overflow-tooltip />
      </el-table>
      <div v-if="!result.rows.length" style="color:#909399;font-size:12px">（无返回行）</div>
    </div>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import request from '../utils/request'

const props = defineProps<{
  visible: boolean
  dataSourceId: string | number
  sql: string
  params: Array<{ name: string; label?: string; type?: string; default?: string }>
}>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void }>()

function onVisibleChange(v: boolean) { emit('update:visible', v) }

const values = ref<Record<string, any>>({})
const loading = ref(false)
const result = ref<any>(null)
const error = ref('')

watch(() => props.visible, v => {
  if (!v) return
  values.value = {}
  for (const p of props.params) values.value[p.name] = p.default || ''
  result.value = null
  error.value = ''
})

async function runTest() {
  loading.value = true
  error.value = ''
  result.value = null
  try {
    const params: Record<string, any> = {}
    for (const p of props.params) {
      const v = values.value[p.name]
      params[p.name] = p.type === 'int' ? (Number(v) || 0) : (v || '')
    }
    const res: any = await request.post('/report/dataview/test-sql', {
      dataSourceId: props.dataSourceId, sql: props.sql, params
    })
    result.value = res.data
  } catch (e: any) {
    error.value = e?.message || '测试失败'
  } finally { loading.value = false }
}
</script>

<style scoped>
.test-error { margin-top: 10px; padding: 8px 12px; background: #fef0f0; border: 1px solid #fbc4c4; border-radius: 6px; color: #f56c6c; font-size: 12px; white-space: pre-wrap; word-break: break-all; }
</style>
