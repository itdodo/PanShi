import { del, get, post, put } from '../../api/http'
import type { PageQuery, PagedResult, VoidResult } from '../../api/types'

/** 字典（/sys/dict：类型 + 数据项）契约预声明 */
export interface DictTypeDto {
  id: string
  dictType: string
  dictName: string
  status?: number
  remark?: string | null
  createTime: string
  version: number
}

/** 后端 DictDataDto：{ id, dictTypeId, label, value, sort, status, tagType, version }（字段名不是 dictLabel/dictValue） */
export interface DictItemDto {
  id: string
  dictTypeId: string
  label: string
  value: string
  /** 标签色（Naive ui tag type：default/info/success/warning/error；种子里的 danger 由 tagTypeOf 归一为 error） */
  tagType?: string | null
  sort: number
  status: number
  remark?: string | null
  version?: number
}

export function pageDictTypes(query: PageQuery & { keyword?: string }): Promise<PagedResult<DictTypeDto>> {
  return get<PagedResult<DictTypeDto>>('/sys/dict/type/page', query)
}

/** 按类型取启用字典项（表单下拉/标签渲染高频接口，失败静默） */
export function getDictItems(dictType: string): Promise<DictItemDto[]> {
  return get<DictItemDto[]>(`/sys/dict/data/${dictType}`, undefined, { silent: true })
}

export function createDictType(dto: Partial<DictTypeDto>): Promise<string> {
  return post<string>('/sys/dict/type', dto)
}

export function updateDictType(dto: Partial<DictTypeDto>): Promise<VoidResult> {
  return put<VoidResult>('/sys/dict/type', dto)
}

export function deleteDictType(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/dict/type/${id}`)
}

export function createDictItem(dto: Partial<DictItemDto>): Promise<string> {
  return post<string>('/sys/dict/data', dto)
}

export function updateDictItem(dto: Partial<DictItemDto>): Promise<VoidResult> {
  return put<VoidResult>('/sys/dict/data', dto)
}

export function deleteDictItem(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/dict/data/${id}`)
}
