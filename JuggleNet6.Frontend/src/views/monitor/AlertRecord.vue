<template>
  <div class="page-container">
    <div class="page-header">
      <h2>告警记录</h2>
      <el-button size="small" @click="loadData">刷新</el-button>
    </div>
    <el-card>
      <el-table :data="tableData" v-loading="loading">
        <el-table-column prop="id" label="ID" width="70" />
        <el-table-column prop="ruleName" label="规则名称" width="160" />
        <el-table-column label="指标类型" width="110">
          <template #default="{ row }"><el-tag size="small">{{ metricLabel(row.metricType) }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="message" label="告警信息" show-overflow-tooltip />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.status === 'triggered' ? 'danger' : 'success'" size="small">
              {{ row.status === 'triggered' ? '已触发' : '已恢复' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="时间" width="170" />
      </el-table>
      <el-pagination v-model:current-page="page" :page-size="20" layout="prev,next" :total="total" @change="loadData" style="margin-top:12px;justify-content:flex-end" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import request from '../../utils/request'

const loading = ref(false)
const tableData = ref<any[]>([])
const page = ref(1)
const total = ref(0)

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res = await request.get('/alert/record/list', { params: { page: page.value } })
    tableData.value = res.data?.list || []
    total.value = res.data?.total || 0
  } finally { loading.value = false }
}

function metricLabel(t: string) {
  const m: Record<string, string> = { flow_fail: '流程失败', api_fail: '接口失败', flow_timeout: '流程超时', api_timeout: '接口超时' }
  return m[t] || t
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
</style>
