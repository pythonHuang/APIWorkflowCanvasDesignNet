<template>
  <el-dialog :model-value="visible" @update:model-value="onVisibleChange" title="🚀 分享到官方市场（GitHub）" width="560px" append-to-body>
    <div style="margin-bottom:10px;color:#606266;font-size:13px;line-height:1.7">
      将当前条目以 <b>JSON 文件</b> 形式生成 <b>Pull Request</b> 到官方市场仓库
      <b>pythonHuang/APIWorkflowCanvasDesignNet</b> 的 <b>market/{type}/{id}.json</b>，
      管理员定期审核合并后进入官方市场，其他用户可通过「发现」拉取。
    </div>
    <el-form label-width="90px" size="small">
      <el-form-item label="GitHub Token">
        <el-input v-model="form.token" type="password" show-password placeholder="ghp_...（仅保存在本机浏览器）" />
        <div style="font-size:11px;color:#909399;line-height:1.6">
          需要 Personal Access Token（classic）并勾选 <b>repo</b> 权限：GitHub → Settings → Developer settings → Tokens。仅用于本次分享操作，保存在 localStorage。
        </div>
      </el-form-item>
      <el-form-item label="作者"><el-input v-model="form.author" placeholder="您的署名（如 pythonHuang）" /></el-form-item>
      <el-form-item label="版本号"><el-input v-model="form.version" placeholder="如 1.0.0" /></el-form-item>
      <el-form-item label="条目">
        <div style="font-size:13px;color:#303133">
          <span style="margin-right:4px">{{ props.icon || '📦' }}</span>{{ props.itemName }}
          <el-tag size="small" style="margin-left:6px">{{ typeLabel }}</el-tag>
        </div>
      </el-form-item>
    </el-form>
    <el-alert v-if="shareResult" :type="shareResult.ok ? 'success' : 'error'" :closable="false" style="margin-bottom:8px">
      <template #title>{{ shareResult.msg }}</template>
    </el-alert>
    <template #footer>
      <el-button size="small" @click="emit('update:visible', false)">关闭</el-button>
      <el-button size="small" type="primary" :loading="sharing" @click="doShare">🚀 生成 Pull Request</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'

const props = defineProps<{
  visible: boolean
  itemType: string
  itemName: string
  description?: string
  icon?: string
  contentJson: string
}>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void; (e: 'shared', url: string): void }>()

function onVisibleChange(v: boolean) { emit('update:visible', v) }

const GITHUB_API = 'https://api.github.com'
const REPO_OWNER = 'pythonHuang'
const REPO_NAME = 'APIWorkflowCanvasDesignNet'
const TOKEN_KEY = 'market_github_token'

const typeLabels: Record<string, string> = { api: '接口', flow: '流程', assistant: '模型助手', skill: 'Skill', report: '报表' }
const typeLabel = computed(() => typeLabels[props.itemType] || props.itemType)

const form = ref({ token: localStorage.getItem(TOKEN_KEY) || '', author: '', version: '1.0.0' })
const sharing = ref(false)
const shareResult = ref<{ ok: boolean; msg: string } | null>(null)

async function gh(path: string, token: string, opts?: { method?: string; body?: any }) {
  const res = await fetch(`${GITHUB_API}${path}`, {
    headers: {
      Authorization: `token ${token}`,
      Accept: 'application/vnd.github+json',
      'Content-Type': 'application/json'
    },
    method: opts?.method || 'GET',
    body: opts?.body ? JSON.stringify(opts.body) : undefined
  })
  const data = await res.json().catch(() => ({}))
  if (!res.ok) throw new Error(data?.message || `GitHub API 返回 ${res.status}`)
  return data
}

function sleep(ms: number) { return new Promise(r => setTimeout(r, ms)) }

async function doShare() {
  const token = form.value.token.trim()
  if (!token) { ElMessage.warning('请填写 GitHub 访问令牌'); return }
  if (!form.value.author.trim()) { ElMessage.warning('请填写作者'); return }
  localStorage.setItem(TOKEN_KEY, token)

  sharing.value = true
  shareResult.value = null
  try {
    // 1. 校验令牌并获取登录用户
    const user = await gh('/user', token)
    const username = user.login

    // 2. 确保 fork 存在（不存在则创建并轮询等待）
    let fork = await gh(`/repos/${username}/${REPO_NAME}`, token).catch(() => null)
    if (!fork) {
      await gh(`/repos/${REPO_OWNER}/${REPO_NAME}/forks`, token, { method: 'POST' })
      for (let i = 0; i < 20; i++) {
        await sleep(3000)
        fork = await gh(`/repos/${username}/${REPO_NAME}`, token).catch(() => null)
        if (fork) break
      }
      if (!fork) throw new Error('Fork 创建超时，请稍后在 GitHub 页面确认后重试')
    }

    // 3. 基于 fork 默认分支创建分享分支
    const baseBranch = fork.default_branch || 'main'
    const baseRef = await gh(`/repos/${username}/${REPO_NAME}/git/ref/heads/${baseBranch}`, token)
    const shareId = Math.floor(Date.now() / 1000)
    const branch = `market-share-${props.itemType}-${shareId}`
    try {
      await gh(`/repos/${username}/${REPO_NAME}/git/refs`, token, {
        method: 'POST',
        body: { ref: `refs/heads/${branch}`, sha: baseRef.object.sha }
      })
    } catch { /* 分支已存在则直接复用 */ }

    // 4. 组装条目 JSON 并写入 market/{type}/{id}.json
    let content: any = {}
    try { content = JSON.parse(props.contentJson || '{}') } catch { content = {} }
    const itemJson = JSON.stringify({
      id: shareId,
      type: props.itemType,
      name: props.itemName,
      description: props.description || '',
      icon: props.icon || '📦',
      author: form.value.author.trim(),
      version: form.value.version.trim() || '1.0.0',
      updatedAt: new Date().toISOString().slice(0, 10),
      content
    }, null, 2)
    const filePath = `market/${props.itemType}/${shareId}.json`
    await gh(`/repos/${username}/${REPO_NAME}/contents/${encodeURIComponent(filePath)}`, token, {
      method: 'PUT',
      body: {
        message: `market: add ${typeLabel.value}「${props.itemName}」 by ${form.value.author.trim()}`,
        content: btoa(unescape(encodeURIComponent(itemJson))),
        branch
      }
    })

    // 5. 创建 Pull Request
    const pr = await gh(`/repos/${REPO_OWNER}/${REPO_NAME}/pulls`, token, {
      method: 'POST',
      body: {
        title: `[市场] ${typeLabel.value}：${props.itemName}（by ${form.value.author.trim()}）`,
        head: `${username}:${branch}`,
        base: baseBranch,
        body: [
          `分享类型：${typeLabel.value}`,
          `名称：${props.itemName}`,
          `作者：${form.value.author.trim()}`,
          `版本：${form.value.version.trim() || '1.0.0'}`,
          `描述：${props.description || ''}`,
          '',
          `文件：${filePath}`,
          '',
          '管理员审核合并后进入官方市场（market 目录），其他用户可通过「市场 → 发现」拉取。'
        ].join('\n')
      }
    })

    shareResult.value = { ok: true, msg: `PR 创建成功：${pr.html_url}` }
    emit('shared', pr.html_url)
    window.open(pr.html_url, '_blank')
  } catch (e: any) {
    shareResult.value = { ok: false, msg: `分享失败：${e?.message || '未知错误'}` }
  } finally {
    sharing.value = false
  }
}
</script>
