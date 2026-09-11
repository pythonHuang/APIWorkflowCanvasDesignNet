<template>
  <div class="page-container">
    <div class="page-header">
      <h2>🔴 Redis 配置</h2>
      <el-button type="primary" size="small" @click="openAdd">添加实例</el-button>
    </div>
    <el-card>
      <el-table :data="tableData" v-loading="loading">
        <el-table-column prop="configName" label="实例名称" width="160" />
        <el-table-column prop="host" label="地址" min-width="180" show-overflow-tooltip />
        <el-table-column prop="port" label="端口" width="80" />
        <el-table-column prop="db" label="库" width="60" />
        <el-table-column label="默认" width="90" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.isDefault === 1" type="success" size="small">默认</el-tag>
            <el-button v-else size="small" link @click="setDefault(row)">设为默认</el-button>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="180">
          <template #default="{ row }">
            <el-button size="small" link @click="doTest(row)">测试</el-button>
            <el-button size="small" link @click="openEdit(row)">编辑</el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div style="margin-top:10px;color:#909399;font-size:12px">
        可配置多个 Redis 实例（一个设为默认）。流程设计器 Redis 查询/设置节点可按实例选择（0=默认实例），未配置时执行对应节点会报错提示。
      </div>
      <div v-if="testMsg" :style="`font-size:12px;margin-top:8px;color:${testOk ? '#67c23a' : '#f56c6c'}`">{{ testMsg }}</div>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="form.id ? '编辑实例' : '添加实例'" width="520px">
      <el-form :model="form" label-width="100px">
        <el-form-item label="实例名称"><el-input v-model="form.configName" placeholder="如 缓存主库" /></el-form-item>
        <el-form-item label="Redis地址"><el-input v-model="form.host" placeholder="如 127.0.0.1" /></el-form-item>
        <el-form-item label="端口"><el-input v-model="form.port" placeholder="6379" /></el-form-item>
        <el-form-item label="密码"><el-input v-model="form.password" type="password" show-password placeholder="无密码留空" /></el-form-item>
        <el-form-item label="数据库"><el-input v-model="form.db" placeholder="0" /></el-form-item>
        <el-form-item label="默认实例"><el-switch v-model="form.isDefault" /></el-form-item>
      </el-form>
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
const form = ref<any>({ id: 0, configName: '', host: '', port: '6379', password: '', db: '0', isDefault: false })
const testMsg = ref('')
const testOk = ref(false)

onMounted(loadData)

async function loadData() {
  loading.value = true
  try {
    const res: any = await request.get('/system/redis/configs')
    tableData.value = res.data || []
  } finally { loading.value = false }
}

function openAdd() {
  form.value = { id: 0, configName: '', host: '', port: '6379', password: '', db: '0', isDefault: tableData.value.length === 0 }
  dialogVisible.value = true
}
function openEdit(row: any) {
  form.value = { ...row, isDefault: row.isDefault === 1 }
  dialogVisible.value = true
}
async function doSave() {
  await request.post('/system/redis/config/save', form.value)
  ElMessage.success('保存成功')
  dialogVisible.value = false
  loadData()
}
async function setDefault(row: any) {
  await request.post('/system/redis/config/save', { ...row, isDefault: true })
  ElMessage.success(`已设为默认：${row.configName}`)
  loadData()
}
async function doTest(row: any) {
  testMsg.value = ''
  try {
    const res: any = await request.post('/system/redis/config/test', row)
    testOk.value = true
    testMsg.value = `${row.configName || row.host}：${res.data}`
  } catch (e: any) {
    testOk.value = false
    testMsg.value = e?.message || '连接失败'
  }
}
async function doDelete(row: any) {
  await ElMessageBox.confirm(`确认删除实例「${row.configName || row.host}」？`, '提示', { type: 'warning' })
  await request.delete(`/system/redis/config/${row.id}`)
  ElMessage.success('已删除')
  loadData()
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
</style>
