<template>
  <el-dialog :model-value="visible" @update:model-value="onVisibleChange" title="🛒 发布到市场" width="520px" append-to-body>
    <el-form label-width="90px" size="small">
      <el-form-item label="名称"><el-input v-model="form.itemName" /></el-form-item>
      <el-form-item label="描述"><el-input v-model="form.description" type="textarea" :rows="2" placeholder="用途与亮点说明" /></el-form-item>
      <el-form-item label="分组"><el-input v-model="form.groupName" placeholder="可选" /></el-form-item>
    </el-form>
    <div style="color:#909399;font-size:12px">发布后条目进入市场，平台内其他租户可浏览并一键导入。</div>
    <template #footer>
      <el-button size="small" @click="emit('update:visible', false)">取消</el-button>
      <el-button size="small" type="primary" :loading="publishing" @click="doPublish">发布</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import request from '../utils/request'

const props = defineProps<{
  visible: boolean
  itemType: string
  defaultName?: string
  defaultDesc?: string
  defaultGroup?: string
  contentJson: string
}>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void; (e: 'published'): void }>()

function onVisibleChange(v: boolean) { emit('update:visible', v) }

const form = ref({ itemName: '', description: '', groupName: '' })
const publishing = ref(false)

watch(() => props.visible, v => {
  if (!v) return
  form.value = {
    itemName: props.defaultName || '',
    description: props.defaultDesc || '',
    groupName: props.defaultGroup || ''
  }
})

async function doPublish() {
  if (!form.value.itemName.trim()) { ElMessage.warning('请填写名称'); return }
  publishing.value = true
  try {
    await request.post('/market/publish', {
      itemType: props.itemType,
      itemName: form.value.itemName,
      description: form.value.description,
      groupName: form.value.groupName,
      contentJson: props.contentJson
    })
    ElMessage.success('已发布到市场')
    emit('update:visible', false)
    emit('published')
  } catch { /* 拦截器已提示 */ } finally { publishing.value = false }
}
</script>
