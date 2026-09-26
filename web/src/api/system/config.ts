import { del, get, post, put } from '../../api/http'
import type { PageQuery, PagedResult, VoidResult } from '../../api/types'

/** 参数配置（/sys/config）契约预声明：sys.captcha.enabled、sys.pwd.* 等 */
export interface ConfigDto {
  id: string
  configKey: string
  configValue: string
  configName: string
  /** 内置参数禁止删除 */
  builtIn?: boolean
  remark?: string | null
  createTime: string
  version: number
}

export function pageConfigs(query: PageQuery & { keyword?: string }): Promise<PagedResult<ConfigDto>> {
  return get<PagedResult<ConfigDto>>('/sys/config/page', query)
}

/** 后端是 [HttpPut("{id:long}")]，路径必须带 id；dto 需含 version（乐观锁） */
export function updateConfig(id: string, dto: Partial<ConfigDto>): Promise<VoidResult> {
  return put<VoidResult>(`/sys/config/${id}`, dto)
}

export function createConfig(dto: Partial<ConfigDto>): Promise<string> {
  return post<string>('/sys/config', dto)
}

export function deleteConfig(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/config/${id}`)
}
