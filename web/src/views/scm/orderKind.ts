import { pagePurchases } from '@/api/biz'
import {
  createPurchaseOrder,
  createSalesOrder,
  deletePurchaseOrder,
  deleteSalesOrder,
  getPurchaseOrder,
  getSalesOrder,
  pagePurchaseOrders,
  pageSalesOrders,
  submitPurchaseOrder,
  submitSalesOrder,
  updatePurchaseOrder,
  updateSalesOrder,
  type OrderLineForm,
  type OrderLineDto,
  type ScmDocQuery
} from '@/api/scm'
import { customerOptions, priceQuote, supplierOptions, warehouseOptions } from '@/api/basedata'
import type { PagedResult, VoidResult } from '@/api/types'

/**
 * 采购/销售订单页的共用描述符：两张单据的差别只有「往来单位是谁、第二个业务字段是什么、
 * 调哪组接口」，把差异收进这里，页面本体只写一遍——两份 300 行近似代码迟早改一处漏一处。
 */
export interface OrderRow {
  id: string
  docNo: string
  ownerUserId: string
  ownerUserName: string
  orderDate: string
  deliveryDate?: string | null
  totalQty: number
  totalAmount: number
  status: number
  instanceId?: string | null
  remark?: string | null
  createTime: string
  version: number
  lineCount: number
  lines: OrderLineDto[]
  /** 两种单据各自的往来单位/附加外键都在这里，靠访问器取值，页面本体不区分 kind */
  supplierId?: string | null
  supplierName?: string
  customerId?: string | null
  customerName?: string
  sourceRequestId?: string | null
  sourceRequestNo?: string | null
  warehouseId?: string | null
  warehouseName?: string | null
}

export interface Option {
  value: string
  label: string
}

/** 表单模型：日期一律 YYYY-MM-DD 字符串（后端 DateTime? 直接吃），id 用字符串防雪花精度丢失 */
export interface OrderFormModel {
  partnerId: string
  orderDate: string
  deliveryDate: string | null
  extraId: string | null
  remark: string | null
  lines: OrderLineForm[]
  version?: number
}

export interface OrderKind {
  title: string
  /** 权限码前缀，如 scm:purchase */
  perm: string
  partnerLabel: string
  /** 列表查询里往来单位的参数名（supplierId / customerId） */
  partnerFieldName: 'supplierId' | 'customerId'
  extraLabel: string
  /** 采购列显示来源申请单号，销售列显示发货仓库名 */
  extraOf: (row: OrderRow) => string | null
  partnerOf: (row: OrderRow) => string
  partnerIdOf: (row: OrderRow) => string | null
  extraIdOf: (row: OrderRow) => string | null
  partnerOptions: () => Promise<Option[]>
  extraOptions: () => Promise<Option[]>
  /** 选料后带出协议价（只有采购有协议价） */
  quote: (partnerId: string, materialId: string) => Promise<{ unitPrice: number; taxRate: number } | null>
  page: (q: ScmDocQuery) => Promise<PagedResult<OrderRow>>
  detail: (id: string) => Promise<OrderRow>
  create: (d: OrderFormModel) => Promise<OrderRow>
  update: (id: string, d: OrderFormModel) => Promise<VoidResult>
  remove: (id: string) => Promise<VoidResult>
  submit: (id: string) => Promise<OrderRow>
}

/** 已通过的采购申请单（status=2）才是合法来源 */
const sourceRequestOptions = async (): Promise<Option[]> => {
  const page = await pagePurchases({ pageNum: 1, pageSize: 200, status: 2 })
  return page.rows.map((r) => ({ value: r.id, label: `${r.docNo} ${r.itemName}` }))
}

export const purchaseKind: OrderKind = {
  title: '采购订单',
  perm: 'scm:purchase',
  partnerLabel: '供应商',
  partnerFieldName: 'supplierId',
  extraLabel: '来源申请单',
  extraOf: (row) => row.sourceRequestNo ?? null,
  partnerOf: (row) => row.supplierName ?? '',
  partnerIdOf: (row) => row.supplierId ?? null,
  extraIdOf: (row) => row.sourceRequestId ?? null,
  partnerOptions: supplierOptions,
  extraOptions: sourceRequestOptions,
  quote: async (partnerId, materialId) => {
    if (!partnerId) return null
    const hit = await priceQuote(partnerId, materialId)
    return hit ? { unitPrice: hit.unitPrice, taxRate: hit.taxRate } : null
  },
  page: pagePurchaseOrders,
  detail: getPurchaseOrder,
  create: (d) =>
    createPurchaseOrder({
      supplierId: d.partnerId,
      orderDate: d.orderDate,
      deliveryDate: d.deliveryDate,
      sourceRequestId: d.extraId,
      remark: d.remark,
      lines: d.lines,
      version: d.version
    }),
  update: (id, d) =>
    updatePurchaseOrder(id, {
      supplierId: d.partnerId,
      orderDate: d.orderDate,
      deliveryDate: d.deliveryDate,
      sourceRequestId: d.extraId,
      remark: d.remark,
      lines: d.lines,
      version: d.version
    }),
  remove: deletePurchaseOrder,
  submit: submitPurchaseOrder
}

export const salesKind: OrderKind = {
  title: '销售订单',
  perm: 'scm:sales',
  partnerLabel: '客户',
  partnerFieldName: 'customerId',
  extraLabel: '发货仓库',
  extraOf: (row) => row.warehouseName ?? null,
  partnerOf: (row) => row.customerName ?? '',
  partnerIdOf: (row) => row.customerId ?? null,
  extraIdOf: (row) => row.warehouseId ?? null,
  partnerOptions: customerOptions,
  extraOptions: warehouseOptions,
  quote: async () => null,
  page: pageSalesOrders,
  detail: getSalesOrder,
  create: (d) =>
    createSalesOrder({
      customerId: d.partnerId,
      orderDate: d.orderDate,
      deliveryDate: d.deliveryDate,
      warehouseId: d.extraId,
      remark: d.remark,
      lines: d.lines,
      version: d.version
    }),
  update: (id, d) =>
    updateSalesOrder(id, {
      customerId: d.partnerId,
      orderDate: d.orderDate,
      deliveryDate: d.deliveryDate,
      warehouseId: d.extraId,
      remark: d.remark,
      lines: d.lines,
      version: d.version
    }),
  remove: deleteSalesOrder,
  submit: submitSalesOrder
}
