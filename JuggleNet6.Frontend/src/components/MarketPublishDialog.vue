<template>
  <el-dialog :model-value="visible" @update:model-value="onVisibleChange" title="🛒 发布到市场" width="520px" append-to-body>
    <el-form label-width="90px" size="small">
      <el-form-item label="名称"><el-input v-model="form.itemName" /></el-form-item>
      <el-form-item label="描述"><el-input v-model="form.description" type="textarea" :rows="2" placeholder="用途与亮点说明" /></el-form-item>
      <el-form-item label="分组"><el-input v-model="form.groupName" placeholder="可选" /></el-form-item>
    </el-form>
    <div style="color:#909399;font-size:12px">
      发布后条目进入平台市场，其他租户可浏览并一键导入；发布成功后可选择<b>分享到官方 GitHub 市场</b>（生成 PR 供管理员审核）。
    </div>
    <template #footer>
      <el-button size="small" @click="emit('update:visible', false)">取消</el-button>
      <el-button size="small" type="primary" :loading="publishing" @click="doPublish">发布</el-button>
    </template>
  </el-dialog>

  <!-- 分享到官方 GitHub 市场（PR） -->
  <MarketShareDialog v-model:visible="shareVisible" :item-type="props.itemType" :item-name="form.itemName"
    :description="form.description" :icon="props.icon" :content-json="props.contentJson" />
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import request from '../utils/request'
import MarketShareDialog from './MarketShareDialog.vue'

const props = defineProps<{
  visible: boolean
  itemType: string
  defaultName?: string
  defaultDesc?: string
  defaultGroup?: string
  icon?: string
  contentJson: string
}>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void; (e: 'published'): void }>()

function onVisibleChange(v: boolean) { emit('update:visible', v) }

const form = ref({ itemName: '', description: '', groupName: '' })
const publishing = ref(false)
const shareVisible = ref(false)

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
    emit('update:visible', false)
    emit('published')
    try {
      await ElMessageBox.confirm(
        '已发布到平台市场！是否同时分享到官方 GitHub 市场？将自动生成 Pull Request，管理员审核合并后进入官方市场。',
        '分享到官方市场', { confirmButtonText: '分享', cancelButtonText: '暂不', type: 'info' })
      shareVisible.value = true
    } catch { /* 暂不分享 */ }
  } catch { /* 拦截器已提示 */ } finally { publishing.value = false }
}
</script>
