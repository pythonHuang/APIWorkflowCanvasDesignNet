<template>
  <div class="page-container">
    <div class="page-header">
      <h2>API 拓扑图</h2>
      <span style="font-size:12px;color:#888">
        <span class="status-dot" style="background:#52c41a"></span> 正常
        <span class="status-dot" style="background:#faad14;margin-left:8px"></span> 告警
        <span class="status-dot" style="background:#ff4d4f;margin-left:8px"></span> 离线
      </span>
      <el-button size="small" @click="loadData" :loading="loading">刷新</el-button>
    </div>
    <div style="display:flex;height:calc(100% - 50px);gap:12px">
      <div style="flex:1;border:1px solid #e0e0e0;border-radius:8px;overflow:hidden">
        <VueFlow v-model:nodes="vfNodes" v-model:edges="vfEdges" :default-viewport="{ x: 0, y: 0, zoom: 1 }" :min-zoom="0.2" :max-zoom="2" fit-view-on-init>
          <Background :variant="'dots'" :gap="20" :size="1" :color="'#ddd'" />
          <template #node-custom="{ data }">
            <div :class="['topo-node', 'topo-' + data.status, data.nodeType === 'db' ? 'topo-db' : '']" @click="selectHost(data)">
              <div class="topo-node-title">🗄️ {{ data.label }}</div>
              <div class="topo-node-count" v-if="data.nodeType === 'db'">
                {{ data.dbType }} | 调用 {{ data.totalCalls }} 次
              </div>
              <div class="topo-node-count" v-else>
                {{ data.apiCount }} 个接口 | 调用 {{ data.totalCalls }} 次
              </div>
            </div>
          </template>
        </VueFlow>
      </div>
      <div v-if="selectedHost" style="width:380px;border:1px solid #e0e0e0;border-radius:8px;padding:12px;overflow-y:auto">
        <h4 style="margin:0 0 4px">{{ selectedHost }}</h4>
        <div style="font-size:12px;color:#888;margin-bottom:8px">
          <el-tag size="small" :type="selectedNodeType === 'db' ? 'warning' : 'info'" style="margin-right:8px">{{ selectedNodeType === 'db' ? '数据库' : 'API节点' }}</el-tag>
          总调用 {{ selectedStats?.totalCalls || 0 }} 次 |
          成功 {{ selectedStats?.totalSuccess || 0 }} |
          失败 {{ selectedStats?.totalFail || 0 }}
          <el-tag size="small" :type="selectedStats?.status === 'offline' ? 'danger' : selectedStats?.status === 'warning' ? 'warning' : 'success'" style="margin-left:8px">
            {{ selectedStats?.status === 'offline' ? '离线' : selectedStats?.status === 'warning' ? '告警' : '正常' }}
          </el-tag>
        </div>
        <!-- API节点：显示接口列表 -->
        <template v-if="selectedNodeType === 'api'">
          <div v-for="api in selectedApis" :key="api.id" style="padding:8px;margin-bottom:4px;border:1px solid #eee;border-radius:4px">
            <div style="display:flex;gap:6px;align-items:center;flex-wrap:wrap">
              <el-tag :type="api.status === 1 ? 'success' : 'danger'" size="small">{{ api.status === 1 ? '启用' : '停用' }}</el-tag>
              <el-tag size="small">{{ api.requestType }}</el-tag>
              <span style="font-weight:500">{{ api.methodName }}</span>
            </div>
            <div style="font-size:12px;color:#888;margin-top:2px">{{ api.url }}</div>
            <div style="font-size:11px;margin-top:2px">
              调用 <b>{{ api.callCount || 0 }}</b> 次 |
              成功 <b style="color:#52c41a">{{ api.successCount || 0 }}</b> |
              失败 <b style="color:#ff4d4f">{{ api.failCount || 0 }}</b>
            </div>
          </div>
          <el-empty v-if="selectedApis.length === 0" description="该节点无接口" />
        </template>
        <!-- 数据库节点：显示DB调用统计 -->
        <template v-if="selectedNodeType === 'db'">
          <div style="padding:8px;background:#fafafa;border-radius:4px">
            <p><b>数据库名：</b>{{ selectedStats?.dbName }}</p>
            <p><b>类型：</b>{{ selectedStats?.dbType }}</p>
            <p><b>地址：</b>{{ selectedStats?.dbHost }}</p>
          </div>
        </template>
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
const selectedStats = ref<any>(null)
const selectedApis = ref<any[]>([])
const selectedNodeType = ref('')
const vfNodes = ref<any[]>([])
const vfEdges = ref<any[]>([])
const allNodes = ref<any[]>([])

onMounted(loadData)

function statusColor(s: string) {
  return s === 'offline' ? '#ff4d4f' : s === 'warning' ? '#faad14' : '#52c41a'
}

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.get('/monitor/topology')
    if (res.data?.nodes) {
      allNodes.value = res.data.nodes
      vfNodes.value = res.data.nodes.map((n: any, i: number) => ({
        id: n.id,
        type: 'custom',
        position: { x: (i % 4) * 240, y: Math.floor(i / 4) * 140 },
        data: { label: n.label, apiCount: n.apiCount, status: n.status, hostId: n.id, apis: n.apis || [], totalCalls: n.totalCalls || 0, totalSuccess: n.totalSuccess || 0, totalFail: n.totalFail || 0, nodeType: n.nodeType || 'api', dbName: n.dbName, dbType: n.dbType, dbHost: n.dbHost }
      }))
      vfEdges.value = (res.data.edges || []).map((e: any, i: number) => {
        const color = statusColor(e.status || 'online')
        return {
          id: `e${i}`,
          source: e.source,
          target: e.target,
          label: e.label,
          labelBgStyle: { fill: '#fff' },
          labelStyle: { fontSize: '10px', fill: '#666' },
          style: { stroke: color, strokeWidth: 2 },
          markerEnd: { type: 'arrowclosed' as any, width: 14, height: 14, color },
          animated: true
        }
      })
    }
  } finally { loading.value = false }
}

function selectHost(data: any) {
  selectedHost.value = data.hostId
  selectedStats.value = data
  selectedApis.value = data.apis || []
  selectedNodeType.value = data.nodeType || 'api'
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;align-items:center;gap:12px;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
.status-dot { display:inline-block;width:10px;height:10px;border-radius:50% }
.topo-node { padding:12px 16px;border-radius:8px;text-align:center;cursor:pointer;min-width:150px;border:2px solid #52c41a;background:#f6ffed }
.topo-node.topo-warning { border-color:#faad14;background:#fffbe6 }
.topo-node.topo-offline { border-color:#ff4d4f;background:#fff2f0 }
.topo-node-title { font-weight:600;font-size:13px;word-break:break-all }
.topo-node-count { font-size:11px;color:#888;margin-top:4px }
</style>
