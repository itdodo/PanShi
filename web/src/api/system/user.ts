import { del, get, post, put } from '../../api/http'
import type { PageQuery, PagedResult, VoidResult } from '../../api/types'

/**
 * 用户管理（/sys/user）。后端批次 #6 并行开发中，按蓝图 API 一览预声明契约；
 * 所有 id 一律字符串（雪花）。
 */
export interface UserDto {
  id: string
  userName: string
  nickName: string
  phone?: string | null
  email?: string | null
  avatarFileId?: string | null
  /** 0 正常 / 1 停用 */
  status: number
  deptId?: string | null
  deptName?: string | null
  roleNames?: string | null
  remark?: string | null
  lastLoginTime?: string | null
  createTime: string
  version: number
}

export interface UserFormDto {
  id?: string
  userName: string
  nickName: string
  password?: string
  phone?: string | null
  email?: string | null
  status: number
  deptId?: string | null
  positionIds?: string[]
  roleIds?: string[]
  remark?: string | null
  version?: number
}

export interface UserQuery extends PageQuery {
  keyword?: string
  deptId?: string | null
  status?: number | null
}

export function pageUsers(query: UserQuery): Promise<PagedResult<UserDto>> {
  return get<PagedResult<UserDto>>('/sys/user/page', query)
}

export function getUser(id: string): Promise<UserDto> {
  return get<UserDto>(`/sys/user/${id}`)
}

export function createUser(dto: UserFormDto): Promise<string> {
  return post<string>('/sys/user', dto)
}

export function updateUser(dto: UserFormDto): Promise<VoidResult> {
  return put<VoidResult>('/sys/user', dto)
}

export function deleteUsers(ids: string[]): Promise<VoidResult> {
  return del<VoidResult>('/sys/user', { data: ids })
}

export function resetPassword(id: string, password: string): Promise<VoidResult> {
  return post<VoidResult>('/sys/user/password/reset', { id, password })
}

/** 批量角色分配（防提权：仅内置管理员可操作 admin） */
export function assignRoles(id: string, roleIds: string[]): Promise<VoidResult> {
  return post<VoidResult>('/sys/user/roles', { id, roleIds })
}
