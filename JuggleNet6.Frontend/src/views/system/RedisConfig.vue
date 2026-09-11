<template>
  <div class="page-container">
    <div class="page-header">
      <h2>🔴 Redis 配置</h2>
    </div>
    <el-card style="max-width:560px">
      <el-form :model="form" label-width="100px">
        <el-form-item label="Redis地址"><el-input v-model="form.host" placeholder="如 127.0.0.1 或 redis.example.com" /></el-form-item>
        <el-form-item label="端口"><el-input v-model="form.port" placeholder="6379" /></el-form-item>
        <el-form-item label="密码"><el-input v-model="form.password" type="password" show-password placeholder="无密码留空" /></el-form-item>
        <el-form-item label="数据库"><el-input v-model="form.db" placeholder="0" /></el-form-item>
        <el-form-item>
          <el-button type="primary" @click="doSave">保存</el-button>
          <el-button :loading="testing" @click="doTest">测试连接</el-button>
        </el-form-item>
      </el-form>
      <div v-if="testMsg" :style="`font-size:12px;margin-top:4px;color:${testOk ? '#67c23a' : '#f56c6c'}`">{{ testMsg }}</div>
      <div style="margin-top:12px;color:#909399;font-size:12px">
        流程设计器的 Redis 查询/Redis 设置节点使用此配置。未配置时执行对应节点会报错提示。
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import request from '../../utils/request'

const form = ref<any>({ host: '', port: '6379', password: '', db: '0' })
const testing = ref(false)
const testMsg = ref('')
const testOk = ref(false)

onMounted(async () => {
  try {
    const res: any = await request.get('/system/redis/config')
    if (res.data) form.value = res.data
  } catch { /* 未配置 */ }
})

async function doSave() {
  await request.post('/system/redis/config', form.value)
  ElMessage.success('Redis 配置已保存')
}

async function doTest() {
  testing.value = true
  testMsg.value = ''
  try {
    const res: any = await request.post('/system/redis/test', form.value)
    testOk.value = true
    testMsg.value = res.data || '连接成功'
  } catch (e: any) {
    testOk.value = false
    testMsg.value = e?.message || '连接失败'
  } finally { testing.value = false }
}
</script>

<style scoped>
.page-container { padding:16px;height:100%;display:flex;flex-direction:column;box-sizing:border-box }
.page-header { display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;flex-shrink:0 }
.page-header h2 { margin:0 }
</style>
