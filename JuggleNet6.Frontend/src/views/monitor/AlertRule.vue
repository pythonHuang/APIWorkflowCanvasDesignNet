<template>
  <div class="page-container">
    <div class="page-header">
      <h2>告警规则</h2>
      <el-button type="primary" size="small" @click="openAdd">添加规则</el-button>
    </div>
    <el-card>
      <el-table :data="tableData" v-loading="loading">
        <el-table-column prop="name" label="规则名称" />
        <el-table-column label="监控指标" width="130">
          <template #default="{ row }"><el-tag size="small">{{ metricLabel(row.metricType) }}</el-tag></template>
        </el-table-column>
        <el-table-column label="条件" width="160">
          <template #default="{ row }">{{ row.condition }} {{ row.threshold }}</template>
        </el-table-column>
        <el-table-column label="通知方式" width="120">
          <template #default="{ row }"><el-tag :type="row.channel === 'sms' ? 'warning' : 'info'" size="small">{{ row.channel }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="recipients" label="接收人" />
        <el-table-column label="状态" width="70" align="center">
          <template #default="{ row }">
            <el-switch v-model="row.status" :active-value="1" :inactive-value="0" size="small" @change="doToggle(row)" />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="120">
          <template #default="{ row }">
            <el-button size="small" link @click="openEdit(row)">编辑</el-button>
            <el-button size="small" type="danger" link @click="doDelete(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="isEdit ? '编辑规则' : '添加规则'" width="520px">
      <el-form :model="form" label-width="80px">
        <el-form-item label="规则名称"><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="监控指标">
          <el-select v-model="form.metricType" style="width:100%">
            <el-option value="flow_fail" label="流程失败" />
            <el-option value="api_fail" label="接口调用失败" />
            <el-option value="flow_timeout" label="流程超时" />
            <el-option value="api_timeout" label="接口超时" />
          </el-select>
        </el-form-item>
        <el-form-item label="触发条件">
          <el-select v-model="form.condition" style="width:90px">
            <el-option value=">" label=">" />
            <el-option value=">=" label=">=" />
          </el-select>
          <el-input-number v-model="form.threshold" :min="0" style="margin-left:8px;width:120px" />
          <span style="margin-left:4px;font-size:12px;color:#888">次/10分钟</span>
        </el-form-item>
        <el-form-item label="通知方式">
          <el-select v-model="form.channel" style="width:100%">
            <el-option value="email" label="邮件" />
            <el-option value="sms" label="短信" />
            <el-option value="email+sms" label="邮件+短信" />
          </el-select>
        </el-form-item>
        <el-form-item label="接收人"><el-input v-model="form.recipients" placeholder="邮箱或手机号，多个逗号分隔" /></el-form-item>
        <el-form-item label="描述"><el-input v-model="form.description" type="textarea" :rows="2" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="doSubmit">保存</el-button>
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
const isEdit = ref(false)
const form = ref({ id: 0, name: '', metricType: 'flow_fail', condition: '>', threshold: 3, channel: 'email', recipients: '', description: '', status: 1 })

onMounted(loadData)

async function loadData() {
  loading.value = true
  try { const res = await request.get('/alert/rule/list'); tableData.value = res.data || [] } finally { loading.value = false }
}

function metricLabel(t: string) {
  const m: Record<string, string> = { flow_fail: '流程失败', api_fail: '接口失败', flow_timeout: '流程超时', api_timeout: '接口超时' }
  return m[t] || t
}

function openAdd() {
  isEdit.value = false
  form.value = { id: 0, name: '', metricType: 'flow_fail', condition: '>', threshold: 3, channel: 'email', recipients: '', description: '', status: 1 }
  dialogVisible.value = true
}

function openEdit(row: any) {
  isEdit.value = true
  form.value = { ...row }
  dialogVisible.value = true
}

async function doSubmit() {
  if (isEdit.value) {
    await request.put('/alert/rule/update', form.value)
  } else {
    await request.post('/alert/rule/add', form.value)
  }
  ElMessage.success('保存成功')
  dialogVisible.value = false
  loadData()
}

async function doToggle(row: any) {
  await request.put('/alert/rule/update', row)
}

async function doDelete(row: any) {
  await ElMessageBox.confirm('确认删除该规则？', '提示', { type: 'warning' })
  await request.delete(`/alert/rule/delete/${row.id}`)
  ElMessage.success('已删除')
  loadData()
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
</style>
