import { del, get, post, put } from './http'
import type { PageQuery, PagedResult, VoidResult } from './types'

/**
 * 审批流真实契约（后端批次 #7 已落地联调通过）。
 * 任务状态 status：1待办 2同意 3拒绝 4转办 5自动通过 6失效 7等待 8已驳回
 * 实例状态 status：1审批中 2通过 3拒绝 4撤回 5作废
 */

export const TASK_STATUS = {
  Pending: 1, Agreed: 2, Rejected: 3, Transferred: 4,
  AutoPassed: 5, Invalidated: 6, Waiting: 7, Returned: 8
} as const

export const INSTANCE_STATUS = {
  Running: 1, Approved: 2, Rejected: 3, Withdrawn: 4, Voided: 5
} as const

export interface FlowDefDto {
  id: string
  flowCode: string
  flowName: string
  category?: string | null
  nodeJson: string
  flowVersion: number
  status: number
  remark?: string | null
  createTime: string
  version: number
}

export interface FlowDefQuery extends PageQuery {
  keyword?: string
  category?: string | null
  status?: number | null
}

export interface FlowDefForm {
  flowName: string
  category?: string | null
  nodeJson: string
  remark?: string | null
  version?: number
}

export interface FlowBindingDto {
  id: string
  businessTable: string
  flowCode: string
  status: number
  remark?: string | null
  flowName?: string | null
  version: number
}

export interface FlowTaskDto {
  id: string
  instanceId: string
  nodeCode: string
  nodeName: string
  nodeMode: string
  approverUserId: string
  approverName: string
  status: number
  sequence: number
  comment?: string | null
  handledTime?: string | null
  createTime: string
  flowName: string
  summary?: string | null
  submitterName: string
  businessTable: string
  businessId: string
  instanceStatus: number
}

export interface FlowInstanceDto {
  id: string
  flowCode: string
  flowName: string
  businessTable: string
  businessId: string
  summary?: string | null
  currentNodeCode?: string | null
  status: number
  submitterId: string
  submitterName: string
  createTime: string
  finishedTime?: string | null
}

export interface FlowRecordDto {
  id: string
  nodeCode?: string | null
  nodeName?: string | null
  action: string
  operatorName?: string | null
  comment?: string | null
  createTime: string
}

export interface FlowInstanceDetail {
  instance: FlowInstanceDto
  tasks: FlowTaskDto[]
  records: FlowRecordDto[]
  nodeJson: string
}

export interface FlowCcDto {
  id: string
  instanceId: string
  nodeCode?: string | null
  isRead: boolean
  createTime: string
  instance?: FlowInstanceDto | null
}

export interface FlowSubmitDto {
  businessTable: string
  businessId: number
  variables: Record<string, unknown>
  choiceUserIds?: string[]
}

export interface FlowInstanceQuery extends PageQuery {
  flowCode?: string
  status?: number | null
  keyword?: string
}

export interface FlowTaskQuery extends PageQuery {
  keyword?: string
}

/* ---------------- 定义/绑定 ---------------- */
export const pageFlowDefs = (q: FlowDefQuery) => get<PagedResult<FlowDefDto>>('/sys/flow/def/page', q)
export const flowCategories = () => get<string[]>('/sys/flow/def/categories')
export const defVersions = (code: string) => get<FlowDefDto[]>(`/sys/flow/def/versions/${code}`)
export const createFlowDef = (d: FlowDefForm) => post<FlowDefDto>('/sys/flow/def', d)
export const updateFlowDef = (id: string, d: FlowDefForm) => put<FlowDefDto>(`/sys/flow/def/${id}`, d)
export const enableFlowDef = (id: string, status: number) =>
  post<VoidResult>(`/sys/flow/def/${id}/enable`, { status })
export const deleteFlowDef = (id: string) => del<VoidResult>(`/sys/flow/def/${id}`)

export const listFlowBindings = () => get<FlowBindingDto[]>('/sys/flow/binding')
export const saveFlowBinding = (d: {
  businessTable: string; flowCode: string; status: number; remark?: string | null; version?: number
}) => put<VoidResult>('/sys/flow/binding', d)
export const deleteFlowBinding = (id: string) => del<VoidResult>(`/sys/flow/binding/${id}`)

/* ---------------- 任务 ---------------- */
export const pageTodo = (q: FlowTaskQuery) => get<PagedResult<FlowTaskDto>>('/sys/flow/task/todo', q)
export const pageDone = (q: FlowTaskQuery) => get<PagedResult<FlowTaskDto>>('/sys/flow/task/done', q)
/** 静默轮询版本（顶栏角标用，后端未就绪时不弹错） */
export const todoCount = () => get<number>('/sys/flow/task/todo-count', undefined, { silent: true })
export const actTask = (id: string, action: 'approve' | 'reject', comment?: string) =>
  post<VoidResult>(`/sys/flow/task/${id}/act`, { action, comment })
export const returnTask = (id: string, targetNodeCode: string, comment?: string) =>
  post<VoidResult>(`/sys/flow/task/${id}/return`, { targetNodeCode, comment })
export const transferTask = (id: string, toUserId: string, comment?: string) =>
  post<VoidResult>(`/sys/flow/task/${id}/transfer`, { toUserId: Number(toUserId), comment })
export const addsignTask = (id: string, userIds: string[], after: boolean, comment?: string) =>
  post<VoidResult>(`/sys/flow/task/${id}/addsign`, { after, userIds: userIds.map(Number), comment })

/* ---------------- 实例 ---------------- */
/** 提交（返回实例 id；data 为空串=未绑定流程已直通，msg 提示） */
export const submitFlow = (d: FlowSubmitDto) => post<string>('/sys/flow/submit', d)
export const pageInstances = (q: FlowInstanceQuery) =>
  get<PagedResult<FlowInstanceDto>>('/sys/flow/instance/page', q)
export const pageMyInstances = (q: FlowInstanceQuery) =>
  get<PagedResult<FlowInstanceDto>>('/sys/flow/instance/my', q)
export const instanceDetail = (id: string) => get<FlowInstanceDetail>(`/sys/flow/instance/${id}`)
export const instanceByBusiness = (table: string, businessId: string) =>
  get<FlowInstanceDto | null>(`/sys/flow/instance/by-business/${table}/${businessId}`, undefined, { silent: true })
export const withdrawInstance = (id: string) => post<VoidResult>(`/sys/flow/instance/${id}/withdraw`)
export const voidInstance = (id: string, reason?: string) =>
  post<VoidResult>(`/sys/flow/instance/${id}/void`, undefined, { params: { reason } })
export const pageCcMe = (q: PageQuery) => get<PagedResult<FlowCcDto>>('/sys/flow/cc-me', q)
export const markCcRead = (id: string) => post<VoidResult>(`/sys/flow/cc-me/${id}/read`)
