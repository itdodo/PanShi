import { get, post, put, del } from './http'
import type { PageQuery, PagedResult, VoidResult } from './types'

/**
 * 供应链订单（/scm/*）。单据状态枚举与标签复用 @/api/biz 的 DOC_STATUS 与 @/components/flowEnums 的 docStatusMeta。
 * 明细行由前端提交、金额与合计一律由后端算回来（Amount/TotalAmount 只读），
 * 否则前后端四舍五入口径一旦分叉，对账时才发现单头合计和明细加起来差一分。
 */
export interface OrderLineDto {
  id: string
  materialId: string
  materialCode: string
  materialName: string
  spec?: string | null
  unit?: string | null
  quantity: number
  unitPrice: number
  taxRate: number
  amount: number
  remark?: string | null
}

export interface OrderLineForm {
  materialId: string
  quantity: number
  unitPrice: number
  taxRate: number
  remark?: string | null
}

/** 明细行物料下拉项：value=id，label=编码+名称(+规格)，spec/unit 用于选中后带出只读列 */
export interface MaterialChoice {
  value: string
  label: string
  spec?: string | null
  unit?: string | null
}

export interface ScmDocQuery extends PageQuery {
  keyword?: string
  status?: number | null
  mine?: boolean
  supplierId?: string | null
  customerId?: string | null
  begin?: string
  end?: string
}

export interface PurchaseOrderDto {
  id: string
  docNo: string
  ownerUserId: string
  ownerUserName: string
  supplierId?: string | null
  supplierName: string
  orderDate: string
  deliveryDate?: string | null
  totalQty: number
  totalAmount: number
  sourceRequestId?: string | null
  sourceRequestNo?: string | null
  status: number
  instanceId?: string | null
  remark?: string | null
  createTime: string
  version: number
  lineCount: number
  lines: OrderLineDto[]
}

export interface PurchaseOrderForm {
  supplierId: string
  orderDate: string
  deliveryDate?: string | null
  sourceRequestId?: string | null
  remark?: string | null
  lines: OrderLineForm[]
  version?: number
}

export interface SalesOrderDto {
  id: string
  docNo: string
  ownerUserId: string
  ownerUserName: string
  customerId?: string | null
  customerName: string
  orderDate: string
  deliveryDate?: string | null
  totalQty: number
  totalAmount: number
  warehouseId?: string | null
  warehouseName?: string | null
  status: number
  instanceId?: string | null
  remark?: string | null
  createTime: string
  version: number
  lineCount: number
  lines: OrderLineDto[]
}

export interface SalesOrderForm {
  customerId: string
  orderDate: string
  deliveryDate?: string | null
  warehouseId?: string | null
  remark?: string | null
  lines: OrderLineForm[]
  version?: number
}

/** 提交审批：businessTable/businessId 后端会覆盖，这里按引擎入参形状带上 */
const submitPayload = (id: string) => ({
  businessTable: '',
  businessId: Number(id),
  variables: {},
  choiceUserIds: []
})

export const pagePurchaseOrders = (q: ScmDocQuery) => get<PagedResult<PurchaseOrderDto>>('/scm/purchase-order/page', q)
export const getPurchaseOrder = (id: string) => get<PurchaseOrderDto>(`/scm/purchase-order/${id}`)
export const createPurchaseOrder = (d: PurchaseOrderForm) => post<PurchaseOrderDto>('/scm/purchase-order', d)
export const updatePurchaseOrder = (id: string, d: PurchaseOrderForm) =>
  put<VoidResult>(`/scm/purchase-order/${id}`, d)
export const deletePurchaseOrder = (id: string) => del<VoidResult>(`/scm/purchase-order/${id}`)
export const submitPurchaseOrder = (id: string) =>
  post<PurchaseOrderDto>(`/scm/purchase-order/${id}/submit`, { ...submitPayload(id), businessTable: 'scm_purchase_order' })

export const pageSalesOrders = (q: ScmDocQuery) => get<PagedResult<SalesOrderDto>>('/scm/sales-order/page', q)
export const getSalesOrder = (id: string) => get<SalesOrderDto>(`/scm/sales-order/${id}`)
export const createSalesOrder = (d: SalesOrderForm) => post<SalesOrderDto>('/scm/sales-order', d)
export const updateSalesOrder = (id: string, d: SalesOrderForm) => put<VoidResult>(`/scm/sales-order/${id}`, d)
export const deleteSalesOrder = (id: string) => del<VoidResult>(`/scm/sales-order/${id}`)
export const submitSalesOrder = (id: string) =>
  post<SalesOrderDto>(`/scm/sales-order/${id}/submit`, { ...submitPayload(id), businessTable: 'scm_sales_order' })

/** 行金额口径与后端一致：数量×单价，四舍五入到分（half-up） */
export function lineAmount(qty: number, price: number): number {
  const raw = (Number(qty) || 0) * (Number(price) || 0)
  return Math.round((raw + Number.EPSILON) * 100) / 100
}
