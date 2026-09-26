import { del, get, post, put } from '../../api/http'
import type { PageQuery, PagedResult, VoidResult } from '../../api/types'

/**
 * 字典管理（/sys/dict：类型 + 数据项）。后端 PUT 一律带 {id}；
 * 类型编码是 dictCode、数据项是 label/value（不是 dictType/dictLabel/dictValue）。
 */
/** 后端字段：编码是 dictCode，且没有 status */
export interface DictTypeDto {
  id: string
  dictCode: string
  dictName: string
  remark?: string | null
  createTime: string
  version: number
}

/** 后端 DictDataDto：{ id, dictTypeId, label, value, sort, status, tagType, isDefault, version }（字段名不是 dictLabel/dictValue） */
export interface DictItemDto {
  id: string
  dictTypeId: string
  label: string
  value: string
  /** 标签色（Naive ui tag type：default/info/success/warning/error；种子里的 danger 由 tagTypeOf 归一为 error） */
  tagType?: string | null
  sort: number
  status: number
  isDefault?: boolean
  version: number
}

export function pageDictTypes(query: PageQuery & { keyword?: string }): Promise<PagedResult<DictTypeDto>> {
  return get<PagedResult<DictTypeDto>>('/sys/dict/type/page', query)
}

/** 按类型取启用字典项（表单下拉/标签渲染高频接口，失败静默） */
export function getDictItems(dictType: string): Promise<DictItemDto[]> {
  return get<DictItemDto[]>(`/sys/dict/data/${dictType}`, undefined, { silent: true })
}

/** 按类型 id 取数据项（管理端右栏，需 sys:dict:list） */
export function getDictDataByType(typeId: string): Promise<DictItemDto[]> {
  return get<DictItemDto[]>(`/sys/dict/data/type/${typeId}`)
}

export function createDictType(dto: Partial<DictTypeDto>): Promise<string> {
  return post<string>('/sys/dict/type', dto)
}

export function updateDictType(id: string, dto: Partial<DictTypeDto>): Promise<VoidResult> {
  return put<VoidResult>(`/sys/dict/type/${id}`, dto)
}

export function deleteDictType(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/dict/type/${id}`)
}

/**
 * 新增/编辑数据项的载荷。后端 DictDataSaveDto.DictTypeId 是 long，
 * 全局 NumberHandling.AllowReadingFromString 让字符串与数字都能绑定（雪花 id 走字符串防丢精度）。
 */
export interface DictItemFormDto {
  dictTypeId: string | number
  label: string
  value: string
  sort?: number
  status?: number
  tagType?: string | null
  isDefault?: boolean
  version?: number
}

export function createDictItem(dto: DictItemFormDto): Promise<string> {
  return post<string>('/sys/dict/data', dto)
}

export function updateDictItem(id: string, dto: DictItemFormDto): Promise<VoidResult> {
  return put<VoidResult>(`/sys/dict/data/${id}`, dto)
}

export function deleteDictItem(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/dict/data/${id}`)
}
