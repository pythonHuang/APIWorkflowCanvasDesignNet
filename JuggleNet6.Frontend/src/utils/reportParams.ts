/**
 * 报表查询参数工具：
 * - initParamValues：默认值初始化（URL 查询参数优先）并按类型转换
 * - buildParamPayload：查询载荷构建（空值不传；多选/筛选组以逗号拼接）
 */

/** 初始化查询参数值：默认值 → URL 查询参数覆盖 → 按类型转换 */
export function initParamValues(params: any[], urlQuery?: Record<string, any>): Record<string, any> {
  const values: Record<string, any> = {}
  for (const p of params || []) {
    let v: any = p.default
    if (urlQuery && urlQuery[p.name] !== undefined && urlQuery[p.name] !== '') {
      v = urlQuery[p.name]
    }
    if (p.type === 'switch') v = v === true || v === 'true' || v === '1'
    else if (p.type === 'number') v = (v === '' || v === undefined || v === null) ? undefined : Number(v)
    else if (p.type === 'checkgroup') {
      // 筛选组/多选：支持数组或逗号分隔字符串
      v = Array.isArray(v) ? v : String(v ?? '').split(',').map((s: string) => s.trim()).filter(Boolean)
    } else if (p.type === 'select' && p.multiple) {
      v = Array.isArray(v) ? v : (v ? String(v).split(',').map((s: string) => s.trim()).filter(Boolean) : [])
    }
    values[p.name] = v ?? ''
  }
  return values
}

/** 构建查询载荷：空值跳过；筛选组/多选以逗号拼接字符串传给 @参数名 */
export function buildParamPayload(params: any[], values: Record<string, any>): Record<string, any> {
  const payload: Record<string, any> = {}
  for (const p of params || []) {
    let v = values[p.name]
    if (v === undefined || v === null || v === '') continue
    if (Array.isArray(v) && v.length === 0) continue
    if (p.type === 'number') v = Number(v)
    if (p.type === 'switch') v = v === true
    if (Array.isArray(v)) v = v.join(',')
    payload[p.name] = v
  }
  return payload
}

/** 固定选项解析：支持逗号/换行分隔字符串或数组 */
export function parseOptions(p: any): string[] {
  if (Array.isArray(p.options)) return p.options
  return String(p.options || '').split(/[,，\n]/).map((s: string) => s.trim()).filter(Boolean)
}
