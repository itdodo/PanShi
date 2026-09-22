import { get, post, put, del } from './http'
import type { PageQuery, PagedResult, VoidResult } from './types'

/**
 * 业务样板：报销单/采购申请单（真实契约，后端批次 #8 联调通过）。
 * 单据 status：0草稿 1审批中 2通过 3拒绝 4撤回
 */
export const DOC_STATUS = { Draft: 0, Running: 1, Approved: 2, Rejected: 3, Withdrawn: 4 } as const

export interface ExpenseDto {
  id: string
  docNo: string
  ownerUserId: string
  ownerUserName: string
  deptId?: string | null
  amount: number
  category?: string | null
  reason?: string | null
  attachmentIds: string[]
  status: number
  instanceId?: string | null
  createTime: string
  version: number
}
export interface ExpenseForm {
  amount: number
  category?: string | null
  reason?: string | null
  attachmentIds: string[]
  version?: number
}

export interface PurchaseDto {
  id: string
  docNo: string
  ownerUserId: string
  ownerUserName: string
  deptId?: string | null
  itemName: string
  quantity: number
  amount: number
  reason?: string | null
  attachmentIds: string[]
  status: number
  instanceId?: string | null
  createTime: string
  version: number
}
export interface PurchaseForm {
  itemName: string
  quantity: number
  amount: number
  reason?: string | null
  attachmentIds: string[]
  version?: number
}

export interface BizDocQuery extends PageQuery {
  keyword?: string
  status?: number | null
  mine?: boolean
}

export const pageExpenses = (q: BizDocQuery) => get<PagedResult<ExpenseDto>>('/biz/expense/page', q)
export const getExpense = (id: string) => get<ExpenseDto>(`/biz/expense/${id}`)
export const createExpense = (d: ExpenseForm) => post<ExpenseDto>('/biz/expense', d)
export const updateExpense = (id: string, d: ExpenseForm) => put<VoidResult>(`/biz/expense/${id}`, d)
export const deleteExpense = (id: string) => del<VoidResult>(`/biz/expense/${id}`)
/** 提交审批；variables 自动带 amount，choiceUserIds 供自选节点 */
export const submitExpense = (id: string, choiceUserIds?: string[]) =>
  post<ExpenseDto>(`/biz/expense/${id}/submit`, {
    businessTable: 'biz_expense',
    businessId: Number(id),
    variables: {},
    choiceUserIds: choiceUserIds ?? []
  })

export const pagePurchases = (q: BizDocQuery) => get<PagedResult<PurchaseDto>>('/biz/purchase/page', q)
export const getPurchase = (id: string) => get<PurchaseDto>(`/biz/purchase/${id}`)
export const createPurchase = (d: PurchaseForm) => post<PurchaseDto>('/biz/purchase', d)
export const updatePurchase = (id: string, d: PurchaseForm) => put<VoidResult>(`/biz/purchase/${id}`, d)
export const deletePurchase = (id: string) => del<VoidResult>(`/biz/purchase/${id}`)
export const submitPurchase = (id: string, choiceUserIds?: string[]) =>
  post<PurchaseDto>(`/biz/purchase/${id}/submit`, {
    businessTable: 'biz_purchase_request',
    businessId: Number(id),
    variables: {},
    choiceUserIds: choiceUserIds ?? []
  })

/** 文件（通用上传，附件回填 url） */
export interface FileDto {
  id: string
  name: string
  size: number
  url: string
}
export { upload as uploadFile } from './http'
