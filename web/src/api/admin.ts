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

/**
 * IP 黑白名单（/monitor/ip-rule，安全 P1）。
 * 枚举对齐 Panshi.Model：IpRuleKind 1 黑 / 2 白，IpRuleSource 1 人工 / 2 自动，EnableStatus 0 正常 / 1 停用。
 * hitCount 后端是 long → 全局 LongToString 序列化成字符串，页面比较前必须 Number() 化。
 */
export interface IpRuleDto {
  id: string
  cidr: string
  kind: number
  source: number
  status: number
  reason?: string | null
  expiresTime?: string | null
  hitCount: string
  lastHitTime?: string | null
  createTime: string
  version: number
}
export interface IpRuleQuery extends PageQuery {
  keyword?: string
  kind?: number | null
  status?: number | null
}
export interface IpRuleForm {
  cidr: string
  kind: number
  status: number
  reason?: string | null
  /** 本地时区 'YYYY-MM-DD HH:mm:ss'，与后端 DateTime.Now 同基准 */
  expiresTime?: string | null
  version?: number
}
/** DryRun / Enabled 来自 appsettings（不在 sys_config，管理端改不了），页面只做只读提示 */
export interface IpGuardStatus {
  enabled: boolean
  dryRun: boolean
}
export const pageIpRules = (q: IpRuleQuery) => get<PagedResult<IpRuleDto>>('/monitor/ip-rule/page', q)
export const ipGuardStatus = () => get<IpGuardStatus>('/monitor/ip-rule/status')
export const createIpRule = (d: IpRuleForm) => post<IpRuleDto>('/monitor/ip-rule', d)
export const updateIpRule = (id: string, d: IpRuleForm) => put<VoidResult>(`/monitor/ip-rule/${id}`, d)
export const deleteIpRule = (id: string) => del<VoidResult>(`/monitor/ip-rule/${id}`)

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
