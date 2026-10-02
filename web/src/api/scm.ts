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

/** 提交审批：businessTable/businessId/variables 后端会覆盖或补齐，choiceUserIds 供「发起人自选」节点 */
const submitPayload = (id: string, choiceUserIds?: string[]) => ({
  businessTable: '',
  businessId: Number(id),
  variables: {},
  choiceUserIds: choiceUserIds ?? []
})

export const pagePurchaseOrders = (q: ScmDocQuery) => get<PagedResult<PurchaseOrderDto>>('/scm/purchase-order/page', q)
export const getPurchaseOrder = (id: string) => get<PurchaseOrderDto>(`/scm/purchase-order/${id}`)
export const createPurchaseOrder = (d: PurchaseOrderForm) => post<PurchaseOrderDto>('/scm/purchase-order', d)
export const updatePurchaseOrder = (id: string, d: PurchaseOrderForm) =>
  put<VoidResult>(`/scm/purchase-order/${id}`, d)
export const deletePurchaseOrder = (id: string) => del<VoidResult>(`/scm/purchase-order/${id}`)
export const submitPurchaseOrder = (id: string, choiceUserIds?: string[]) =>
  post<PurchaseOrderDto>(`/scm/purchase-order/${id}/submit`, {
    ...submitPayload(id, choiceUserIds),
    businessTable: 'scm_purchase_order'
  })

export const pageSalesOrders = (q: ScmDocQuery) => get<PagedResult<SalesOrderDto>>('/scm/sales-order/page', q)
export const getSalesOrder = (id: string) => get<SalesOrderDto>(`/scm/sales-order/${id}`)
export const createSalesOrder = (d: SalesOrderForm) => post<SalesOrderDto>('/scm/sales-order', d)
export const updateSalesOrder = (id: string, d: SalesOrderForm) => put<VoidResult>(`/scm/sales-order/${id}`, d)
export const deleteSalesOrder = (id: string) => del<VoidResult>(`/scm/sales-order/${id}`)
export const submitSalesOrder = (id: string, choiceUserIds?: string[]) =>
  post<SalesOrderDto>(`/scm/sales-order/${id}/submit`, {
    ...submitPayload(id, choiceUserIds),
    businessTable: 'scm_sales_order'
  })

/** 行金额口径与后端一致：数量×单价，四舍五入到分（half-up） */
export function lineAmount(qty: number, price: number): number {
  const raw = (Number(qty) || 0) * (Number(price) || 0)
  return Math.round((raw + Number.EPSILON) * 100) / 100
}

/* ------------------------------- 库存三块 ------------------------------- */

/** 单据类型：1 采购入库 2 销售出库 3 其他入库 4 其他出库 5 调拨 6 盘点（前缀 RK/CK/QRK/QCK/DB/PD） */
export const STOCK_KINDS = [
  { label: '采购入库', value: 1 },
  { label: '销售出库', value: 2 },
  { label: '其他入库', value: 3 },
  { label: '其他出库', value: 4 },
  { label: '调拨', value: 5 },
  { label: '盘点', value: 6 }
] as const

/** 单据状态：0 草稿（不碰库存）/ 1 已过账（锁定）/ 2 已作废（按流水反向冲销） */
export const STOCK_STATUS = { Draft: 0, Posted: 1, Void: 2 } as const

export const stockKindLabel = (kind?: number | null): string =>
  STOCK_KINDS.find((k) => k.value === kind)?.label ?? '未知'

export function stockStatusMeta(status?: number | null): { label: string; type: 'default' | 'warning' | 'success' | 'error' } {
  if (status === STOCK_STATUS.Posted) return { label: '已过账', type: 'success' }
  if (status === STOCK_STATUS.Void) return { label: '已作废', type: 'error' }
  return { label: '草稿', type: 'warning' }
}

export interface StockDocLineDto {
  id: string
  materialId: string
  materialCode: string
  materialName: string
  spec?: string | null
  unit?: string | null
  quantity: number
  /** 盘点单回填的当前账面数，用于显示差异 */
  bookQty?: number | null
  remark?: string | null
}

export interface StockDocDto {
  id: string
  docNo: string
  kind: number
  warehouseId: string
  warehouseName: string
  targetWarehouseId?: string | null
  targetWarehouseName?: string | null
  bizDate: string
  sourceOrderNo?: string | null
  totalQty: number
  status: number
  postedTime?: string | null
  ownerUserId: string
  ownerUserName: string
  remark?: string | null
  createTime: string
  version: number
  lineCount: number
  lines: StockDocLineDto[]
}

export interface StockDocLineForm {
  materialId: string
  quantity: number
  remark?: string | null
}

export interface StockDocForm {
  kind: number
  warehouseId: string
  targetWarehouseId?: string | null
  bizDate: string
  sourceOrderNo?: string | null
  remark?: string | null
  lines: StockDocLineForm[]
  version?: number
}

export interface StockDocQuery extends PageQuery {
  keyword?: string
  kind?: number | null
  status?: number | null
  warehouseId?: string | null
  mine?: boolean
  begin?: string
  end?: string
}

export const pageStockDocs = (q: StockDocQuery) => get<PagedResult<StockDocDto>>('/scm/stock-doc/page', q)
export const getStockDoc = (id: string) => get<StockDocDto>(`/scm/stock-doc/${id}`)
export const createStockDoc = (d: StockDocForm) => post<StockDocDto>('/scm/stock-doc', d)
export const updateStockDoc = (id: string, d: StockDocForm) => put<VoidResult>(`/scm/stock-doc/${id}`, d)
export const deleteStockDoc = (id: string) => del<VoidResult>(`/scm/stock-doc/${id}`)
export const postStockDoc = (id: string) => post<StockDocDto>(`/scm/stock-doc/${id}/post`)
export const voidStockDoc = (id: string) => post<StockDocDto>(`/scm/stock-doc/${id}/void`)

export interface StockDto {
  id: string
  warehouseId: string
  warehouseName: string
  materialId: string
  materialCode: string
  materialName: string
  spec?: string | null
  unit?: string | null
  quantity: number
  updateTime?: string | null
}
export interface StockQuery extends PageQuery {
  keyword?: string
  warehouseId?: string | null
  onlyPositive?: boolean
}
export const pageStocks = (q: StockQuery) => get<PagedResult<StockDto>>('/scm/stock/page', q)

export interface LedgerDto {
  id: string
  docId: string
  docNo: string
  kind: number
  warehouseId: string
  warehouseName: string
  materialId: string
  materialCode: string
  materialName: string
  unit?: string | null
  changeQty: number
  beforeQty: number
  afterQty: number
  bizTime: string
  operatorName: string
}
export interface LedgerQuery extends PageQuery {
  keyword?: string
  kind?: number | null
  warehouseId?: string | null
  materialId?: string | null
  begin?: string
  end?: string
}
export const pageLedger = (q: LedgerQuery) => get<PagedResult<LedgerDto>>('/scm/ledger/page', q)

/**
 * 库存预警（/scm/stock-alert）：阈值挂在物料主数据上，后端按「启用仓库 × 设了阈值的物料」展开，
 * 没有台账行的按 0 存量算——从没入过库的新料恰恰最该报警。
 */
export const ALERT_LEVELS = { Short: 1, Over: 2 } as const

export function alertLevelMeta(level?: number | null): { label: string; type: 'warning' | 'error' } {
  return level === ALERT_LEVELS.Over
    ? { label: '超储', type: 'warning' }
    : { label: '缺货', type: 'error' }
}

export interface StockAlertDto {
  warehouseId: string
  warehouseName: string
  materialId: string
  materialCode: string
  materialName: string
  spec?: string | null
  unit?: string | null
  minStock?: number | null
  maxStock?: number | null
  quantity: number
  level: number
  /** 缺货=还差多少到下限；超储=超出上限多少 */
  gap: number
}
export interface StockAlertQuery extends PageQuery {
  keyword?: string
  warehouseId?: string | null
  level?: number | null
}
export const pageStockAlerts = (q: StockAlertQuery) => get<PagedResult<StockAlertDto>>('/scm/stock-alert/page', q)
