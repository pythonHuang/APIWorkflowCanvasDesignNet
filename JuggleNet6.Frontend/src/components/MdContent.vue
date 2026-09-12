<template>
  <div class="md-content" v-html="renderedHtml" @click="onCodeAction" />
</template>

<script setup lang="ts">
import { ref, watch, onMounted, nextTick } from 'vue'
import { marked } from 'marked'
import hljs from 'highlight.js'
import 'highlight.js/styles/github.css'
import katex from 'katex'
import 'katex/dist/katex.min.css'
import mermaid from 'mermaid'
import * as echarts from 'echarts'

const props = defineProps<{ content: string }>()
const renderedHtml = ref('')

mermaid.initialize({ startOnLoad: false, theme: 'default' })

/** 块级公式/行内公式/代码块提取为占位符，避免被 markdown 解析破坏 */
function protectFormulas(text: string): { text: string; blocks: string[] } {
  const blocks: string[] = []
  const push = (html: string) => { blocks.push(html); return `@@BLOCK${blocks.length - 1}@@` }
  // $$...$$ 块级公式
  text = text.replace(/\$\$([\s\S]+?)\$\$/g, (_m, latex: string) => {
    try { return push(`<div class="katex-block">${katex.renderToString(latex.trim(), { throwOnError: false, displayMode: true })}</div>`) }
    catch { return _m }
  })
  // $...$ 行内公式
  text = text.replace(/\$([^\n$]+?)\$/g, (_m, latex: string) => {
    try { return push(`<span class="katex-inline">${katex.renderToString(latex.trim(), { throwOnError: false })}</span>`) }
    catch { return _m }
  })
  return { text, blocks }
}

/** 渲染 markdown：代码高亮（HTML 带复制/预览/下载）+ mermaid/echart 图表 + 公式占位恢复 */
function renderMarkdown(raw: string): string {
  let { text, blocks } = protectFormulas(raw)
  // echart 代码块 → 图表占位符
  const chartBlocks: string[] = []
  text = text.replace(/```echart\s*\n([\s\S]+?)```/g, (_m, code: string) => {
    chartBlocks.push(code.trim())
    return `@@CHART${chartBlocks.length - 1}@@`
  })
  // mermaid 代码块 → 收集（渲染后追加）
  const mermaidBlocks: string[] = []
  text.replace(/```mermaid\s*\n([\s\S]+?)```/g, (_m, code: string) => {
    mermaidBlocks.push(code.trim())
    return ''
  })
  // marked 渲染（自定义 code renderer 加高亮与操作按钮）
  const renderer = new marked.Renderer()
  renderer.code = ({ text: code, lang }) => {
    const langName = (lang || '').split(' ')[0]
    let highlighted = ''
    try { highlighted = langName && hljs.getLanguage(langName) ? hljs.highlight(code, { language: langName }).value : hljs.highlightAuto(code).value }
    catch { highlighted = code.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;') }
    const enc = encodeURIComponent(code)
    // HTML 代码块：复制/预览/下载；其他语言：仅复制
    const actions = langName.toLowerCase() === 'html'
      ? `<button class="copy-btn" data-action="copy" data-code="${enc}">复制</button><button class="copy-btn" data-action="preview" data-code="${enc}">预览</button><button class="copy-btn" data-action="download" data-code="${enc}">下载</button>`
      : `<button class="copy-btn" data-action="copy" data-code="${enc}">复制</button>`
    return `<div class="code-block"><div class="code-head"><span>${langName || 'code'}</span><div class="code-actions">${actions}</div></div><pre><code class="hljs">${highlighted}</code></pre></div>`
  }
  marked.use({ renderer, gfm: true, breaks: true })
  let html = marked.parse(text) as string
  // 恢复公式占位
  blocks.forEach((b, i) => { html = html.replace(`@@BLOCK${i}@@`, b) })
  // 恢复 echart 图表占位
  chartBlocks.forEach((code, i) => {
    html = html.replace(`@@CHART${i}@@`, `<div class="echart-box" data-option="${encodeURIComponent(code)}"></div>`)
  })
  // mermaid 图表（追加到末尾渲染）
  mermaidBlocks.forEach((code, i) => {
    const id = `mermaid_${Date.now()}_${i}`
    html += `<div class="mermaid" id="${id}">${code.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')}</div>`
  })
  return html
}

/** 代码块操作按钮（复制/预览/下载，事件委托） */
function onCodeAction(e: MouseEvent) {
  const btn = (e.target as HTMLElement).closest?.('[data-action]') as HTMLElement | null
  if (!btn) return
  const code = decodeURIComponent(btn.dataset.code || '')
  const action = btn.dataset.action
  if (action === 'copy') {
    navigator.clipboard?.writeText(code).then(
      () => { btn.textContent = '已复制'; setTimeout(() => { btn.textContent = '复制' }, 1500) },
      () => { /* 剪贴板不可用则忽略 */ }
    )
  } else if (action === 'preview') {
    const w = window.open('', '_blank')
    if (!w) return
    w.document.write(code)
    w.document.close()
  } else if (action === 'download') {
    const blob = new Blob([code], { type: 'text/html' })
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = 'preview.html'
    a.click()
    URL.revokeObjectURL(a.href)
  }
}

async function render() {
  renderedHtml.value = renderMarkdown(props.content || '')
  await nextTick()
  // mermaid 渲染
  const mermaidEls = document.querySelectorAll('.mermaid:not([data-processed])')
  for (const el of Array.from(mermaidEls)) {
    try {
      await mermaid.run({ nodes: [el as HTMLElement] })
      el.setAttribute('data-processed', '1')
    } catch { el.innerHTML = '<span style="color:#f56c6c">图表渲染失败</span>' }
  }
  // echart 图表渲染（```echart 代码块内容为 ECharts option JSON）
  const chartEls = document.querySelectorAll('.echart-box:not([data-processed])')
  for (const el of Array.from(chartEls)) {
    try {
      const optionJson = decodeURIComponent((el as HTMLElement).dataset.option || '{}')
      const chart = echarts.init(el as HTMLElement)
      chart.setOption(JSON.parse(optionJson))
      el.setAttribute('data-processed', '1')
    } catch { el.innerHTML = '<span style="color:#f56c6c;padding:8px">图表配置解析失败（```echart 内应为 ECharts option JSON）</span>' }
  }
}

watch(() => props.content, render)
onMounted(render)
</script>

<style scoped>
.md-content { font-size: 13px; line-height: 1.7; word-break: break-word; }
.md-content :deep(h1), .md-content :deep(h2), .md-content :deep(h3) { margin: 8px 0 4px; font-size: 15px; }
.md-content :deep(p) { margin: 4px 0; }
.md-content :deep(ul), .md-content :deep(ol) { padding-left: 20px; margin: 4px 0; }
.md-content :deep(img) { max-width: 100%; border-radius: 6px; }
.md-content :deep(table) { border-collapse: collapse; margin: 6px 0; }
.md-content :deep(th), .md-content :deep(td) { border: 1px solid #e4e7ed; padding: 4px 8px; font-size: 12px; }
.md-content :deep(blockquote) { border-left: 3px solid #dcdfe6; margin: 6px 0; padding: 2px 10px; color: #666; }
.md-content :deep(a) { color: #409eff; }
.code-block { margin: 6px 0; border: 1px solid #e4e7ed; border-radius: 6px; overflow: hidden; }
.code-head { display: flex; justify-content: space-between; align-items: center; padding: 4px 10px; background: #f6f8fa; font-size: 11px; color: #909399; }
.code-actions { display: flex; gap: 6px; }
.copy-btn { border: none; background: #ecf5ff; color: #409eff; font-size: 11px; border-radius: 4px; padding: 2px 8px; cursor: pointer; }
.code-block pre { margin: 0; padding: 8px 10px; overflow-x: auto; background: #fafafa; font-size: 12px; }
.code-block code { font-family: Consolas, Monaco, 'Courier New', monospace; }
.katex-block { overflow-x: auto; padding: 6px 0; }
.md-content :deep(.mermaid) { margin: 8px 0; text-align: center; }
.md-content :deep(.mermaid svg) { max-width: 100%; }
.md-content :deep(.echart-box) { width: 100%; height: 320px; margin: 8px 0; border: 1px solid #f0f2f5; border-radius: 6px; }
</style>
