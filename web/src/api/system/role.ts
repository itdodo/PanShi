import { del, get, post, put } from '../../api/http'
import type { PageQuery, PagedResult, VoidResult } from '../../api/types'

/** 角色管理（/sys/role）契约预声明 */
export interface RoleDto {
  id: string
  roleCode: string
  roleName: string
  /** 数据权限：1 全部 2 本部门及以下 3 本部门 4 仅本人 5 自定义 */
  dataScope?: number
  status: number
  sort?: number
  remark?: string | null
  createTime: string
  version: number
}

export interface RoleFormDto {
  id?: string
  roleCode: string
  roleName: string
  dataScope?: number
  status: number
  sort?: number
  remark?: string | null
  menuIds?: string[]
  deptIds?: string[]
  version?: number
}

export interface RoleQuery extends PageQuery {
  keyword?: string
  status?: number | null
}

export function pageRoles(query: RoleQuery): Promise<PagedResult<RoleDto>> {
  return get<PagedResult<RoleDto>>('/sys/role/page', query)
}

/** 下拉用全量启用角色 */
export function listRoles(): Promise<RoleDto[]> {
  return get<RoleDto[]>('/sys/role/list')
}

export function getRole(id: string): Promise<RoleDto> {
  return get<RoleDto>(`/sys/role/${id}`)
}

export function createRole(dto: RoleFormDto): Promise<string> {
  return post<string>('/sys/role', dto)
}

export function updateRole(dto: RoleFormDto): Promise<VoidResult> {
  return put<VoidResult>('/sys/role', dto)
}

export function deleteRole(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/role/${id}`)
}

export function getRoleMenuIds(id: string): Promise<string[]> {
  return get<string[]>(`/sys/role/${id}/menu-ids`)
}

export function getRoleDeptIds(id: string): Promise<string[]> {
  return get<string[]>(`/sys/role/${id}/dept-ids`)
}
