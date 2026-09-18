/**
 * 模型类型识别（按模型名关键词）：chat 对话 / image 生图 / video 生视频。
 * 与后端 AiService.DetectModelKind 保持一致。
 */
export function detectModelKind(model?: string): 'chat' | 'image' | 'video' {
  const m = (model || '').toLowerCase()
  if (/(t2v|wanx[^a-z0-9]*video|kling|hailuo|pixverse|pika|cogvideo|minimax[^a-z0-9]*video|sora|veo|mochi|ltx|video)/.test(m)) return 'video'
  if (/(wanx|t2i|dall|gpt-image|flux|stable|midjourney|kandinsky|playground|image)/.test(m)) return 'image'
  return 'chat'
}

/** 模型类型标记 emoji（下拉/标签展示用） */
export function modelKindEmoji(model?: string): string {
  const kind = detectModelKind(model)
  return kind === 'image' ? '🎨' : kind === 'video' ? '🎬' : ''
}
