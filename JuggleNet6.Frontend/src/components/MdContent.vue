<template>
  <div class="md-content" v-html="renderedHtml" />
</template>

<script setup lang="ts">
import { ref, watch, onMounted, nextTick } from 'vue'
import { marked } from 'marked'
import hljs from 'highlight.js'
import 'highlight.js/styles/github.css'
import katex from 'katex'
import 'katex/dist/katex.min.css'
import mermaid from 'mermaid'

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

/** 渲染 markdown：代码高亮 + mermaid 图表 + 公式占位恢复 */
function renderMarkdown(raw: string): string {
  const { text, blocks } = protectFormulas(raw)
  // mermaid 代码块 → 占位符
  const mermaidBlocks: string[] = []
  text.replace(/```mermaid\s*\n([\s\S]+?)```/g, (_m, code: string) => {
    mermaidBlocks.push(code.trim())
    return ''
  })
  // marked 渲染（自定义 code renderer 加高亮与复制按钮）
  const renderer = new marked.Renderer()
  renderer.code = ({ text: code, lang }) => {
    const langName = (lang || '').split(' ')[0]
    let highlighted = ''
    try { highlighted = langName && hljs.getLanguage(langName) ? hljs.highlight(code, { language: langName }).value : hljs.highlightAuto(code).value }
    catch { highlighted = code.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;') }
    return `<div class="code-block"><div class="code-head"><span>${langName || 'code'}</span><button class="copy-btn" data-code="${encodeURIComponent(code)}" onclick="navigator.clipboard.writeText(decodeURIComponent(this.dataset.code));this.textContent='已复制';setTimeout(()=>this.textContent='复制',1500)">复制</button></div><pre><code class="hljs">${highlighted}</code></pre></div>`
  }
  marked.use({ renderer, gfm: true, breaks: true })
  let html = marked.parse(text) as string
  // 恢复公式占位
  blocks.forEach((b, i) => { html = html.replace(`@@BLOCK${i}@@`, b) })
  // mermaid 图表
  mermaidBlocks.forEach((code, i) => {
    const id = `mermaid_${Date.now()}_${i}`
    html += `<div class="mermaid" id="${id}">${code.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')}</div>`
  })
  return html
}

async function render() {
  renderedHtml.value = renderMarkdown(props.content || '')
  await nextTick()
  // mermaid 渲染
  const els = document.querySelectorAll('.mermaid:not([data-processed])')
  for (const el of Array.from(els)) {
    try {
      await mermaid.run({ nodes: [el as HTMLElement] })
      el.setAttribute('data-processed', '1')
    } catch { el.innerHTML = '<span style="color:#f56c6c">图表渲染失败</span>' }
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
.copy-btn { border: none; background: #ecf5ff; color: #409eff; font-size: 11px; border-radius: 4px; padding: 2px 8px; cursor: pointer; }
.code-block pre { margin: 0; padding: 8px 10px; overflow-x: auto; background: #fafafa; font-size: 12px; }
.code-block code { font-family: Consolas, Monaco, 'Courier New', monospace; }
.katex-block { overflow-x: auto; padding: 6px 0; }
.md-content :deep(.mermaid) { margin: 8px 0; text-align: center; }
.md-content :deep(.mermaid svg) { max-width: 100%; }
</style>
