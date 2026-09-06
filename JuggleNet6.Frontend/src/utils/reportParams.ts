/**
 * 报表查询参数工具：默认值初始化（URL 参数值优先）与查询载荷构建（空值不传 → 可选过滤）。
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
    values[p.name] = v ?? ''
  }
  return values
}

/** 构建查询载荷：空值跳过（后端跳过绑定 → 未填即不过滤） */
export function buildParamPayload(params: any[], values: Record<string, any>): Record<string, any> {
  const payload: Record<string, any> = {}
  for (const p of params || []) {
    let v = values[p.name]
    if (v === undefined || v === null || v === '') continue
    if (p.type === 'number') v = Number(v)
    if (p.type === 'switch') v = v === true
    payload[p.name] = v
  }
  return payload
}
