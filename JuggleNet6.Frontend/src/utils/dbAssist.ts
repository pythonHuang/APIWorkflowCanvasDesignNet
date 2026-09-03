import request from './request'

/**
 * 生成 SQL 字段中文对照（多行文本：字段=中文注释 一行一条）。
 * 单表查询（无 JOIN）→ 取表字段元数据（含中文注释，无注释则 字段=字段）；
 * 其它情况 → 执行 SQL 按实际查询列生成 字段=字段（有注释用注释）。
 */
export async function generateColumnMapping(opts: {
  dataSourceId: string | number
  dataSourceName: string
  sql: string
  params?: Record<string, any>
}): Promise<string[]> {
  const sql = (opts.sql || '').trim()
  if (!sql) throw new Error('SQL 为空')

  // 单表查询：直接取表字段元数据（含中文注释）
  const tableMatch = sql.match(/\bfrom\s+([A-Za-z0-9_\.]+)/i)
  const hasJoin = /\bjoin\b/i.test(sql)
  if (tableMatch && !hasJoin) {
    const res: any = await request.post('/system/datasource/columns', {
      dataSourceName: opts.dataSourceName, tableName: tableMatch[1]
    })
    const lines = (res.data || []).map((c: any) => `${c.name}=${c.comment || c.name}`)
    if (lines.length) return lines
  }

  // 其它情况：执行 SQL 取实际查询列
  const res2: any = await request.post('/report/dataview/test-sql', {
    dataSourceId: opts.dataSourceId, sql, params: opts.params || {}
  })
  return (res2.data?.columns || []).map((c: any) => `${c.name}=${c.comment || c.name}`)
}
