<template>
  <div class="page-container">
    <div class="page-header">
      <h2>API 拓扑图</h2>
      <el-button size="small" @click="loadData" :loading="loading">刷新</el-button>
    </div>
    <div style="display:flex;height:calc(100% - 50px);gap:12px">
      <div style="flex:1;border:1px solid #e0e0e0;border-radius:8px;overflow:hidden">
        <VueFlow v-model:nodes="vfNodes" v-model:edges="vfEdges" :default-viewport="{ x: 0, y: 0, zoom: 1 }" :min-zoom="0.2" :max-zoom="2" fit-view-on-init>
          <Background :variant="'dots'" :gap="20" :size="1" :color="'#ddd'" />
          <template #node-custom="{ data }">
            <div :class="['topo-node', 'topo-' + data.status]" @click="selectHost(data.hostId)">
              <div class="topo-node-title">{{ data.label }}</div>
              <div class="topo-node-count">{{ data.apiCount }} 个接口</div>
            </div>
          </template>
          <template #edge-custom="{ data }">
            <div style="font-size:10px;color:#666;background:#fff;padding:1px 4px;border-radius:2px;white-space:nowrap">{{ data.label }}</div>
          </template>
        </VueFlow>
      </div>
      <div v-if="selectedHost" style="width:340px;border:1px solid #e0e0e0;border-radius:8px;padding:12px;overflow-y:auto">
        <h4 style="margin:0 0 8px">{{ selectedHost }} 的接口</h4>
        <div v-for="api in selectedApis" :key="api.id" style="padding:8px;margin-bottom:4px;border:1px solid #eee;border-radius:4px">
          <div style="display:flex;gap:6px;align-items:center">
            <el-tag :type="api.status === 1 ? 'success' : 'danger'" size="small">{{ api.status === 1 ? '启用' : '停用' }}</el-tag>
            <el-tag size="small">{{ api.requestType }}</el-tag>
            <span style="font-weight:500">{{ api.methodName }}</span>
          </div>
          <div style="font-size:12px;color:#888;margin-top:4px">{{ api.url }}</div>
        </div>
        <el-empty v-if="selectedApis.length === 0" description="该节点无接口" />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import request from '../../utils/request'
import { VueFlow } from '@vue-flow/core'
import { Background } from '@vue-flow/background'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'

const loading = ref(false)
const selectedHost = ref('')
const selectedApis = ref<any[]>([])
const vfNodes = ref<any[]>([])
const vfEdges = ref<any[]>([])

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.get('/monitor/topology')
    if (res.data?.nodes) {
      vfNodes.value = res.data.nodes.map((n: any, i: number) => ({
        id: n.id,
        type: 'custom',
        position: { x: (i % 4) * 220, y: Math.floor(i / 4) * 120 },
        data: { label: n.label, apiCount: n.apiCount, status: n.status, hostId: n.id }
      }))
      vfEdges.value = (res.data.edges || []).map((e: any, i: number) => ({
        id: `e${i}`,
        source: e.source,
        target: e.target,
        label: e.label,
        style: { stroke: '#1890ff', strokeWidth: 2 },
        animated: true
      }))
    }
  } finally { loading.value = false }
}

function selectHost(hostId: string) {
  selectedHost.value = hostId
  const node = vfNodes.value.find(n => n.id === hostId)
  selectedApis.value = node?.data?.apis || []
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.topo-node { padding:12px 16px;border-radius:8px;text-align:center;cursor:pointer;min-width:140px;border:2px solid #52c41a;background:#f6ffed }
.topo-node.topo-warning { border-color:#faad14;background:#fffbe6 }
.topo-node.topo-offline { border-color:#ff4d4f;background:#fff2f0 }
.topo-node-title { font-weight:600;font-size:13px;word-break:break-all }
.topo-node-count { font-size:11px;color:#888;margin-top:4px }
</style>
