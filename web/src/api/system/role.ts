import { del, get, post, put } from '../../api/http'
import type { PageQuery, PagedResult, VoidResult } from '../../api/types'
import type { Option } from '../admin'

/**
 * 角色管理（/sys/role）。已授权的 menuIds/deptIds 由 GET /sys/role/{id} 一并回传
 * （后端没有 /menu-ids、/dept-ids 这类单独端点，别照着旧预声明找）。
 */
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
  menuIds?: (string | number)[]
  deptIds?: (string | number)[]
  version?: number
}

export interface RoleQuery extends PageQuery {
  keyword?: string
  status?: number | null
}

export function pageRoles(query: RoleQuery): Promise<PagedResult<RoleDto>> {
  return get<PagedResult<RoleDto>>('/sys/role/page', query)
}

/** 下拉用全量启用角色：后端是 List<OptionDto>，不是 RoleDto[] */
export function listRoles(): Promise<Option[]> {
  return get<Option[]>('/sys/role/list')
}

export function getRole(id: string): Promise<RoleDto> {
  return get<RoleDto>(`/sys/role/${id}`)
}

export function createRole(dto: RoleFormDto): Promise<string> {
  return post<string>('/sys/role', dto)
}

/** 后端是 [HttpPut("{id:long}")]，路径必须带 id；已授权菜单/部门随 RoleUpdateDto 一起回传 */
export function updateRole(id: string, dto: RoleFormDto): Promise<VoidResult> {
  return put<VoidResult>(`/sys/role/${id}`, dto)
}

export function deleteRole(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/role/${id}`)
}

/** 单独授权（不动基础信息，故不需要 version） */
export function grantRoleMenus(
  id: string,
  dto: { menuIds: (string | number)[]; dataScope?: number; deptIds?: (string | number)[] }
): Promise<VoidResult> {
  return post<VoidResult>(`/sys/role/${id}/menus`, dto)
}
