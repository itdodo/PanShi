import { get, post, put, del } from './http'
import type { PageQuery, PagedResult, VoidResult } from './types'

/**
 * 基础资料（/md/*，一级菜单「基础资料」）。
 * 三张表同一套形状：编码唯一（软删过滤下）、启用/停用、乐观锁 version 回显。
 * 分类/单位/等级存的是字典 value（字典码见迁移 0004：md_material_category / md_unit /
 * md_supplier_category / md_customer_level），标签渲染由页面自己查字典。
 * ⚠️ id 一律 string：后端全局 LongToString 序列化雪花 id，Number 化会静默丢精度。
 */
export interface MaterialDto {
  id: string
  materialCode: string
  materialName: string
  category?: string | null
  spec?: string | null
  unit?: string | null
  purchasePrice?: number | null
  salePrice?: number | null
  status: number
  remark?: string | null
  createTime: string
  version: number
}
export interface MaterialForm {
  materialCode: string
  materialName: string
  category?: string | null
  spec?: string | null
  unit?: string | null
  purchasePrice?: number | null
  salePrice?: number | null
  status: number
  remark?: string | null
  version?: number
}
export interface MaterialQuery extends PageQuery {
  keyword?: string
  category?: string | null
  status?: number | null
}
export const pageMaterials = (q: MaterialQuery) => get<PagedResult<MaterialDto>>('/md/material/page', q)
export const materialOptions = () => get<{ value: string; label: string }[]>('/md/material/options')
export const createMaterial = (d: MaterialForm) => post<MaterialDto>('/md/material', d)
export const updateMaterial = (id: string, d: MaterialForm) => put<VoidResult>(`/md/material/${id}`, d)
export const deleteMaterial = (id: string) => del<VoidResult>(`/md/material/${id}`)

export interface SupplierDto {
  id: string
  supplierCode: string
  supplierName: string
  shortName?: string | null
  taxNo?: string | null
  contact?: string | null
  phone?: string | null
  email?: string | null
  address?: string | null
  bankName?: string | null
  bankAccount?: string | null
  category?: string | null
  status: number
  remark?: string | null
  createTime: string
  version: number
}
export interface SupplierForm {
  supplierCode: string
  supplierName: string
  shortName?: string | null
  taxNo?: string | null
  contact?: string | null
  phone?: string | null
  email?: string | null
  address?: string | null
  bankName?: string | null
  bankAccount?: string | null
  category?: string | null
  status: number
  remark?: string | null
  version?: number
}
export interface SupplierQuery extends PageQuery {
  keyword?: string
  category?: string | null
  status?: number | null
}
export const pageSuppliers = (q: SupplierQuery) => get<PagedResult<SupplierDto>>('/md/supplier/page', q)
export const supplierOptions = () => get<{ value: string; label: string }[]>('/md/supplier/options')
export const createSupplier = (d: SupplierForm) => post<SupplierDto>('/md/supplier', d)
export const updateSupplier = (id: string, d: SupplierForm) => put<VoidResult>(`/md/supplier/${id}`, d)
export const deleteSupplier = (id: string) => del<VoidResult>(`/md/supplier/${id}`)

export interface CustomerDto {
  id: string
  customerCode: string
  customerName: string
  shortName?: string | null
  taxNo?: string | null
  contact?: string | null
  phone?: string | null
  email?: string | null
  address?: string | null
  level?: string | null
  status: number
  remark?: string | null
  createTime: string
  version: number
}
export interface CustomerForm {
  customerCode: string
  customerName: string
  shortName?: string | null
  taxNo?: string | null
  contact?: string | null
  phone?: string | null
  email?: string | null
  address?: string | null
  level?: string | null
  status: number
  remark?: string | null
  version?: number
}
export interface CustomerQuery extends PageQuery {
  keyword?: string
  level?: string | null
  status?: number | null
}
export const pageCustomers = (q: CustomerQuery) => get<PagedResult<CustomerDto>>('/md/customer/page', q)
export const customerOptions = () => get<{ value: string; label: string }[]>('/md/customer/options')
export const createCustomer = (d: CustomerForm) => post<CustomerDto>('/md/customer', d)
export const updateCustomer = (id: string, d: CustomerForm) => put<VoidResult>(`/md/customer/${id}`, d)
export const deleteCustomer = (id: string) => del<VoidResult>(`/md/customer/${id}`)

/** 三页共用的字典码（值与迁移 0004 里一致，改一处就要改另一处） */
export const MD_DICT = {
  materialCategory: 'md_material_category',
  unit: 'md_unit',
  supplierCategory: 'md_supplier_category',
  customerLevel: 'md_customer_level'
} as const

/** 仓库（/md/warehouse）：出入库与发货仓库的数据源，默认仓全库至多一个 */
export interface WarehouseDto {
  id: string
  warehouseCode: string
  warehouseName: string
  address?: string | null
  contact?: string | null
  phone?: string | null
  isDefault: boolean
  status: number
  remark?: string | null
  createTime: string
  version: number
}
export interface WarehouseForm {
  warehouseCode: string
  warehouseName: string
  address?: string | null
  contact?: string | null
  phone?: string | null
  isDefault: boolean
  status: number
  remark?: string | null
  version?: number
}
export interface WarehouseQuery extends PageQuery {
  keyword?: string
  status?: number | null
}
export const pageWarehouses = (q: WarehouseQuery) => get<PagedResult<WarehouseDto>>('/md/warehouse/page', q)
export const warehouseOptions = () => get<{ value: string; label: string }[]>('/md/warehouse/options')
export const createWarehouse = (d: WarehouseForm) => post<WarehouseDto>('/md/warehouse', d)
export const updateWarehouse = (id: string, d: WarehouseForm) => put<VoidResult>(`/md/warehouse/${id}`, d)
export const deleteWarehouse = (id: string) => del<VoidResult>(`/md/warehouse/${id}`)

/** 采购协议价（/md/price-agreement）：供应商×物料唯一，下单选料时带出价格 */
export interface PriceAgreementDto {
  id: string
  supplierId: string
  supplierName: string
  materialId: string
  materialCode: string
  materialName: string
  unitPrice: number
  taxRate: number
  beginDate?: string | null
  endDate?: string | null
  status: number
  remark?: string | null
  createTime: string
  version: number
}
export interface PriceAgreementForm {
  supplierId: string
  materialId: string
  unitPrice: number
  taxRate: number
  beginDate?: string | null
  endDate?: string | null
  status: number
  remark?: string | null
  version?: number
}
export interface PriceAgreementQuery extends PageQuery {
  supplierId?: string | null
  keyword?: string
  status?: number | null
}
export const pagePriceAgreements = (q: PriceAgreementQuery) =>
  get<PagedResult<PriceAgreementDto>>('/md/price-agreement/page', q)
export const priceQuote = (supplierId: string, materialId: string) =>
  get<PriceAgreementDto | null>('/md/price-agreement/quote', { supplierId, materialId }, { silent: true })
export const createPriceAgreement = (d: PriceAgreementForm) => post<PriceAgreementDto>('/md/price-agreement', d)
export const updatePriceAgreement = (id: string, d: PriceAgreementForm) =>
  put<VoidResult>(`/md/price-agreement/${id}`, d)
export const deletePriceAgreement = (id: string) => del<VoidResult>(`/md/price-agreement/${id}`)
