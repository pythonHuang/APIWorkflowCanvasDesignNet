<template>
  <el-dialog :model-value="visible" @update:model-value="onVisibleChange" title="🚀 分享到官方市场（GitHub）" width="600px" append-to-body>
    <div style="margin-bottom:10px;color:#606266;font-size:13px;line-height:1.7">
      将当前条目以 <b>JSON 文件</b> 形式提交到官方市场仓库
      <b>pythonHuang/APIWorkflowCanvasDesignNet</b> 的 <b>market/{type}/{id}.json</b>，
      管理员定期审核合并后进入官方市场（其他用户可通过「发现」拉取）。
    </div>
    <el-form label-width="90px" size="small">
      <el-form-item label="提交方式">
        <el-radio-group v-model="submitMode">
          <el-radio value="token">Token 自动提交</el-radio>
          <el-radio value="manual">网页手动提交（无需 Token）</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="作者"><el-input v-model="form.author" placeholder="您的署名（如 pythonHuang）" /></el-form-item>
      <el-form-item label="版本号"><el-input v-model="form.version" placeholder="如 1.0.0" /></el-form-item>
      <el-form-item label="条目">
        <div style="font-size:13px;color:#303133">
          <span style="margin-right:4px">{{ props.icon || '📦' }}</span>{{ props.itemName }}
          <el-tag size="small" style="margin-left:6px">{{ typeLabel }}</el-tag>
        </div>
      </el-form-item>
      <el-form-item v-if="submitMode === 'token'" label="GitHub Token">
        <el-input v-model="form.token" type="password" show-password placeholder="ghp_...（仅保存在本机浏览器）" />
        <div style="font-size:11px;color:#909399;line-height:1.6">
          需要 Personal Access Token（classic）并勾选 <b>repo</b> 权限：GitHub → Settings → Developer settings → Tokens。仅用于本次分享操作。
        </div>
      </el-form-item>
    </el-form>

    <!-- Token 模式：分步进度 -->
    <div v-if="shareSteps.length > 0" style="margin-bottom:8px;font-size:12px;color:#606266;line-height:1.8">
      <div v-for="(s, i) in shareSteps" :key="i" :style="`color:${s.ok === undefined ? '#909399' : s.ok ? '#67c23a' : '#f56c6c'}`">
        {{ s.ok === undefined ? '⏳' : s.ok ? '✅' : '❌' }} {{ s.text }}
      </div>
    </div>

    <!-- 网页手动模式：操作指引 -->
    <div v-if="submitMode === 'manual'" style="background:#f5f7fa;border-radius:6px;padding:10px 12px;margin-bottom:8px;font-size:12px;color:#606266;line-height:1.8">
      <b>操作步骤：</b><br />
      ① 点击「打开 GitHub 创建文件页」— 文件内容与提交信息已自动填好（无写权限时 GitHub 会自动 fork 到你的账号并创建分支）<br />
      ② 在页面上点击 <b>Commit changes / Propose changes</b> 提交（PR 标题会自动带入提交信息）<br />
      ③ 点击「打开 PR 提交页」— 或复制下方 PR 标题与描述粘贴到 PR 表单中，确认提交
      <div style="margin-top:4px;color:#909399">提示：手动模式仅提交条目文件；market/index.json 的更新由管理员审核合并时处理，或使用 Token 模式自动同步。</div>
    </div>

    <el-alert v-if="shareResult" :type="shareResult.ok ? 'success' : 'error'" :closable="false" style="margin-bottom:8px">
      <template #title>{{ shareResult.msg }}</template>
    </el-alert>

    <template #footer>
      <template v-if="submitMode === 'manual'">
        <el-button size="small" @click="emit('update:visible', false)">关闭</el-button>
        <el-button size="small" @click="copyPrInfo">📋 复制 PR 标题与描述</el-button>
        <el-button size="small" @click="openPrPage">② 打开 PR 提交页</el-button>
        <el-button size="small" type="primary" :loading="sharing" @click="prepareManual">① 打开 GitHub 创建文件页</el-button>
      </template>
      <template v-else>
        <el-button size="small" @click="emit('update:visible', false)">关闭</el-button>
        <el-button size="small" type="primary" :loading="sharing" @click="doShare">🚀 生成 Pull Request</el-button>
      </template>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import request from '../utils/request'

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

const submitMode = ref<'token' | 'manual'>('token')
const form = ref({ token: localStorage.getItem(TOKEN_KEY) || '', author: '', version: '1.0.0' })
const sharing = ref(false)
const shareResult = ref<{ ok: boolean; msg: string } | null>(null)
const shareSteps = ref<{ text: string; ok?: boolean }[]>([])

// 已生成的分享信息（两种模式共用）
const shareInfo = ref<{ id: number; itemJson: string; prTitle: string; prBody: string; filePath: string } | null>(null)

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

/** base64 编码（支持中文） */
function b64(s: string) { return btoa(unescape(encodeURIComponent(s))) }

/** 组装 PR 标题与描述（两种模式共用） */
function buildPrInfo(id: number, author: string, version: string) {
  const filePath = `market/${props.itemType}/${id}.json`
  const prTitle = `[市场] ${typeLabel.value}：${props.itemName}（by ${author}）`
  const prBody = [
    `分享类型：${typeLabel.value}`,
    `名称：${props.itemName}`,
    `作者：${author}`,
    `版本：${version}`,
    `描述：${props.description || ''}`,
    '',
    `文件：${filePath}`,
    '',
    '管理员审核合并后进入官方市场（market 目录），其他用户可通过「市场 → 发现」拉取。'
  ].join('\n')
  return { filePath, prTitle, prBody }
}

/** ① 生成本地分享文件（后端落盘 market/{type}/{id}.json + 更新 index.json） */
async function generateLocalFile() {
  const res: any = await request.post('/market/generate-share-file', {
    itemType: props.itemType,
    name: props.itemName,
    description: props.description || '',
    icon: props.icon || '📦',
    author: form.value.author.trim(),
    version: form.value.version.trim() || '1.0.0',
    contentJson: props.contentJson
  })
  const { id, itemJson } = res.data || {}
  if (!id || !itemJson) throw new Error('生成分享文件失败')
  const { filePath, prTitle, prBody } = buildPrInfo(id, form.value.author.trim(), form.value.version.trim() || '1.0.0')
  shareInfo.value = { id, itemJson, prTitle, prBody, filePath }
  return shareInfo.value
}

/** 网页手动提交：① 打开 GitHub 创建文件页（内容/提交信息自动填好） */
async function prepareManual() {
  if (!form.value.author.trim()) { ElMessage.warning('请填写作者'); return }
  sharing.value = true
  shareResult.value = null
  try {
    const info = await generateLocalFile()
    const commitMsg = `market: add ${typeLabel.value}「${props.itemName}」 by ${form.value.author.trim()}`
    const url = `https://github.com/${REPO_OWNER}/${REPO_NAME}/new/main`
      + `?filename=${encodeURIComponent(info.filePath)}`
      + `&value=${encodeURIComponent(info.itemJson)}`
      + `&message=${encodeURIComponent(commitMsg)}`
    window.open(url, '_blank')
    shareResult.value = { ok: true, msg: '已打开 GitHub 创建文件页（内容与提交信息已自动填好），提交后请继续第 ② 步打开 PR 提交页' }
  } catch (e: any) {
    shareResult.value = { ok: false, msg: `生成分享文件失败：${e?.message || '未知错误'}` }
  } finally { sharing.value = false }
}

/** 网页手动提交：② 打开 PR 提交页（标题/描述自动填写） */
function openPrPage() {
  const info = shareInfo.value
  if (!info) { ElMessage.warning('请先执行第 ① 步生成分享文件'); return }
  const url = `https://github.com/${REPO_OWNER}/${REPO_NAME}/compare`
    + `?expand=1&title=${encodeURIComponent(info.prTitle)}&body=${encodeURIComponent(info.prBody)}`
  window.open(url, '_blank')
}

/** 复制 PR 标题与描述（供用户粘贴到 PR 表单） */
async function copyPrInfo() {
  const info = shareInfo.value
  if (!info) { ElMessage.warning('请先执行第 ① 步生成分享文件'); return }
  const text = `${info.prTitle}\n\n${info.prBody}`
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('已复制 PR 标题与描述')
  } catch {
    // 剪贴板不可用（非 HTTPS）时降级为提示框
    ElMessageBoxAlert(text)
  }
}

function ElMessageBoxAlert(text: string) {
  // 降级：弹窗展示可复制内容
  const w = window.open('', '_blank')
  if (w) {
    w.document.write(`<pre style="white-space:pre-wrap;font-family:monospace">${text.replace(/</g, '&lt;')}</pre>`)
    w.document.close()
  } else {
    ElMessage.info('请手动复制：' + text)
  }
}

/** Token 自动提交：fork → 分支 → 写条目文件 → 合并官方最新 index.json → 创建 PR */
async function doShare() {
  if (!form.value.author.trim()) { ElMessage.warning('请填写作者'); return }
  if (!form.value.token.trim()) { ElMessage.warning('请填写 GitHub 访问令牌'); return }
  localStorage.setItem(TOKEN_KEY, form.value.token.trim())

  sharing.value = true
  shareResult.value = null
  shareSteps.value = []
  const step = (text: string, ok?: boolean) => shareSteps.value.push({ text, ok })
  const token = form.value.token.trim()

  try {
    step('生成本地 market/{type}/{id}.json 并更新 index.json...')
    const info = await generateLocalFile()
    step(`已生成本地文件 ${info.filePath} 并更新 index.json`, true)

    step('校验 GitHub Token...')
    const user = await gh('/user', token)
    const username = user.login
    step(`GitHub 登录成功（${username}）`, true)

    step('确保仓库 fork 存在...')
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
    step('fork 就绪', true)

    const baseBranch = fork.default_branch || 'main'
    const baseRef = await gh(`/repos/${username}/${REPO_NAME}/git/ref/heads/${baseBranch}`, token)
    const branch = `market-share-${props.itemType}-${info.id}`
    try {
      await gh(`/repos/${username}/${REPO_NAME}/git/refs`, token, {
        method: 'POST',
        body: { ref: `refs/heads/${branch}`, sha: baseRef.object.sha }
      })
    } catch { /* 分支已存在则直接复用 */ }

    step(`写入 ${info.filePath}...`)
    await gh(`/repos/${username}/${REPO_NAME}/contents/${encodeURIComponent(info.filePath)}`, token, {
      method: 'PUT',
      body: {
        message: `market: add ${typeLabel.value}「${props.itemName}」 by ${form.value.author.trim()}`,
        content: b64(info.itemJson),
        branch
      }
    })
    step(`已写入 ${info.filePath}`, true)

    step('更新 market/index.json...')
    const upstreamIndex = await fetchUpstreamIndex()
    const idx = upstreamIndex.findIndex((e: any) =>
      Number(e?.id) === Number(info.id) && e?.type === props.itemType)
    const indexEntry = JSON.parse(info.itemJson)
    const { content, ...indexMeta } = indexEntry
    const indexEntryForIndex = { ...indexMeta, file: info.filePath }
    if (idx >= 0) upstreamIndex[idx] = indexEntryForIndex
    else upstreamIndex.push(indexEntryForIndex)
    const indexJson = JSON.stringify({ items: upstreamIndex }, null, 2)
    const idxFile = await gh(`/repos/${username}/${REPO_NAME}/contents/market/index.json?ref=${encodeURIComponent(baseBranch)}`, token).catch(() => null)
    await gh(`/repos/${username}/${REPO_NAME}/contents/market/index.json`, token, {
      method: 'PUT',
      body: {
        message: `market: update index.json (${typeLabel.value}「${props.itemName}」)`,
        content: b64(indexJson),
        branch,
        ...(idxFile?.sha ? { sha: idxFile.sha } : {})
      }
    })
    step('已更新 market/index.json', true)

    step('创建 Pull Request...')
    const pr = await gh(`/repos/${REPO_OWNER}/${REPO_NAME}/pulls`, token, {
      method: 'POST',
      body: {
        title: info.prTitle,
        head: `${username}:${branch}`,
        base: baseBranch,
        body: info.prBody
      }
    })
    step(`PR 创建成功：${pr.html_url}`, true)
    shareResult.value = { ok: true, msg: `PR 创建成功：${pr.html_url}` }
    emit('shared', pr.html_url)
    window.open(pr.html_url, '_blank')
  } catch (e: any) {
    const msg = e?.message || '未知错误'
    shareSteps.value[shareSteps.value.length - 1] && (shareSteps.value[shareSteps.value.length - 1].ok = false)
    shareResult.value = { ok: false, msg: `分享失败：${msg}` }
  } finally {
    sharing.value = false
  }
}

/** 拉取官方仓库最新 market/index.json 条目列表（不存在则返回空数组） */
async function fetchUpstreamIndex(): Promise<any[]> {
  const res = await fetch(`https://raw.githubusercontent.com/${REPO_OWNER}/${REPO_NAME}/main/market/index.json`)
  if (!res.ok) return []
  const data = await res.json().catch(() => ({}))
  return Array.isArray(data) ? data : (data?.items || [])
}
</script>
