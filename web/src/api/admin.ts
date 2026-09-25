import { download, get, post, put, del } from './http'
import type { PageQuery, PagedResult, VoidResult } from './types'

/** 岗位（/sys/position） */
export interface PositionDto {
  id: string
  positionCode: string
  positionName: string
  sort: number
  status: number
  remark?: string | null
  createTime: string
  version: number
}
export interface PositionQuery extends PageQuery {
  keyword?: string
  status?: number | null
}
export interface PositionForm {
  positionCode: string
  positionName: string
  sort: number
  status: number
  remark?: string | null
  version?: number
}
export const pagePositions = (q: PositionQuery) => get<PagedResult<PositionDto>>('/sys/position/page', q)
export const positionOptions = () => get<{ value: string; label: string }[]>('/sys/position/list')
export const createPosition = (d: PositionForm) => post<PositionDto>('/sys/position', d)
export const updatePosition = (id: string, d: PositionForm) => put<VoidResult>(`/sys/position/${id}`, d)
export const deletePosition = (id: string) => del<VoidResult>(`/sys/position/${id}`)

/** 日志审计（/sys/log） */
export interface OperLogDto {
  id: string
  module: string
  action: string
  method: string
  url: string
  params?: string | null
  userName?: string | null
  ip?: string | null
  elapsedMs: string
  success: boolean
  errorMsg?: string | null
  createTime: string
}
export interface OperLogQuery extends PageQuery {
  module?: string
  userName?: string
  success?: boolean | null
  begin?: string
  end?: string
}
export interface LoginLogDto {
  id: string
  userName: string
  result: string
  success: boolean
  ip?: string | null
  userAgent?: string | null
  createTime: string
}
export interface LoginLogQuery extends PageQuery {
  userName?: string
  success?: boolean | null
  begin?: string
  end?: string
}
export interface ChangeLogDto {
  id: string
  tableName: string
  recordId: string
  /** JSON 字符串：[{field,label,before,after}] */
  changes: string
  userName: string
  createTime: string
}
export interface ChangeLogQuery extends PageQuery {
  tableName?: string
  userName?: string
  recordId?: string
}

export const pageOperLogs = (q: OperLogQuery) => get<PagedResult<OperLogDto>>('/sys/log/operation', q)
export const exportOperLogs = (q: OperLogQuery) => download('/sys/log/operation/export', q, '操作日志.xlsx')
export const cleanOperLogs = (days: number) => del<number>('/sys/log/operation/cleanup', { params: { days } })
export const pageLoginLogs = (q: LoginLogQuery) => get<PagedResult<LoginLogDto>>('/sys/log/login', q)
export const exportLoginLogs = (q: LoginLogQuery) => download('/sys/log/login/export', q, '登录日志.xlsx')
export const cleanLoginLogs = (days: number) => del<number>('/sys/log/login/cleanup', { params: { days } })
export const pageChangeLogs = (q: ChangeLogQuery) => get<PagedResult<ChangeLogDto>>('/sys/log/change', q)
export const cleanChangeLogs = (days: number) => del<number>('/sys/log/change/cleanup', { params: { days } })

/** 监控（/monitor） */
export interface JobDto {
  jobId: string
  name: string
  cron: string
  enabled: boolean
  scheduler: string
}
export interface OnlineDto {
  id: string
  userId: string
  userName?: string | null
  loginIp?: string | null
  userAgent?: string | null
  createTime: string
  expireTime: string
  current?: boolean
}
export interface ServerInfo {
  machineName: string
  os: string
  framework: string
  cpuCores: number
  memWorkingSetMb: number
  memHeapMb: number
  uptimeMin: number
  startupTime: string
  pgVersion: string
  dbSize: string
}
export const listJobs = () => get<JobDto[]>('/monitor/jobs')
export const triggerJob = (id: string) => post<VoidResult>(`/monitor/jobs/${id}/trigger`)
export const toggleJob = (id: string, enable: boolean) =>
  post<VoidResult>(`/monitor/jobs/${id}/toggle`, undefined, { params: { enable } })
export const listOnline = (keyword?: string) => get<OnlineDto[]>('/monitor/online', { keyword })
export const kickOnline = (sessionId: string) => del<VoidResult>(`/monitor/online/${sessionId}`)
export const serverInfo = () => get<ServerInfo>('/monitor/server')
export const hangfireConsoleUrl = '/hangfire'

/** 站内信发送补充（/sys/message send——my 系列已在别处）。id 用字符串，避免雪花 id 走 Number 丢精度 */
export const sendMessage = (d: { receiverIds: (string | number)[]; title: string; content?: string }) =>
  post<VoidResult>('/sys/message/send', d)

/** 通用选项类 */
export interface Option {
  value: string
  label: string
}
export const deptOptions = () => get<Option[]>('/sys/dept/options')
export const roleOptions = () => get<Option[]>('/sys/role/list')
export const userOptions = () => get<Option[]>('/sys/user/options')
export const dictTypeList = () => get<{ id: string; dictName: string; dictCode: string }[]>('/sys/dict/type/list')
