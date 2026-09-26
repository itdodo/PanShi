import { del, get, post, put } from '../../api/http'
import type { PageQuery, PagedResult, VoidResult } from '../../api/types'

/**
 * 用户管理（/sys/user）。字段与路径逐条对过 UserController / UserCreateDto / UserUpdateDto：
 * 所有 id 一律字符串（雪花），写操作路径必带 {id}。
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

/** 后端 UserUpdateDto：登录名与口令不在此列（改密走 reset，登录名建好即固定） */
export interface UserUpdateDto {
  nickName: string
  phone?: string | null
  email?: string | null
  deptId?: string | null
  status: number
  remark?: string | null
  roleIds?: string[]
  positionIds?: string[]
  version: number
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

/** 后端是 [HttpPut("{id:long}")]，路径必须带 id */
export function updateUser(id: string, dto: UserUpdateDto): Promise<VoidResult> {
  return put<VoidResult>(`/sys/user/${id}`, dto)
}

/** 后端只有单条软删，没有批量端点 */
export function deleteUser(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/user/${id}`)
}

/** 重置为系统初始密码（admin 账号后端拒绝）；返回值是新密码明文，供管理员转告 */
export function resetPassword(id: string): Promise<string> {
  return post<string>(`/sys/user/${id}/password/reset`)
}

/** 批量角色分配（防提权：仅内置管理员可操作 admin） */
export function assignRoles(id: string, roleIds: string[], version?: number): Promise<VoidResult> {
  return post<VoidResult>(`/sys/user/${id}/roles`, { roleIds, version })
}
