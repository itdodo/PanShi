/** 后端统一契约类型（所有 id 为字符串：雪花 ID 服务端 LongToString 序列化） */

/** 统一响应包：code=0 成功；401/403/409 同形状且 HTTP 状态同码 */
export interface ApiResult<T = unknown> {
  code: number
  msg: string
  data: T
}

/** 分页结果（rows 表格数据源，total 总数；后端默认 CreateTime desc） */
export interface PagedResult<T> {
  total: number
  rows: T[]
}

/** 分页查询参数（pageNum 从 1 起，pageSize 上限 200） */
export interface PageQuery {
  pageNum: number
  pageSize: number
  sortField?: string
  sortOrder?: SortOrder
}

export type SortOrder = 'asc' | 'desc'

/** 空响应体 */
export type VoidResult = undefined | null | void

/**
 * 业务错误：code!==0（含 401/403/409）统一抛该错误，调用方可按 code/status 分支。
 * 409=乐观锁冲突，403=权限不足，401=登录态失效（http 层已自动刷新重试一次）。
 */
export class BizError extends Error {
  readonly code: number
  readonly status?: number

  constructor(code: number, msg: string, status?: number) {
    super(msg)
    this.name = 'BizError'
    this.code = code
    this.status = status
  }
}

export function isBizError(err: unknown): err is BizError {
  return err instanceof BizError
}

/** 从任意异常里取可读文案（网络异常等兜底） */
export function errorText(err: unknown, fallback = '操作失败，请稍后重试'): string {
  if (isBizError(err)) return err.message || fallback
  if (err instanceof Error && err.message) return err.message
  return fallback
}

/** 乐观锁冲突判定（列表/表单保存后据此提示刷新） */
export function isConflict(err: unknown): boolean {
  return isBizError(err) && (err.code === 409 || err.status === 409)
}
