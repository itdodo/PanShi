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
  remark?: string | null
  /** 角色/岗位 id 由 UserService.ToDto 从关联表补齐，列表与详情都会带（不是可选项） */
  roleIds: string[]
  positionIds: string[]
  createTime: string
  pwdUpdateTime: string
  lastLoginTime?: string | null
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
  positionIds?: (string | number)[]
  roleIds?: (string | number)[]
  remark?: string | null
  version?: number
}

/**
 * 后端 UserUpdateDto：登录名与口令不在此列（改密走 reset，登录名建好即固定）。
 * id 数组用 string | number —— 视图侧 toIds() 只在「安全整数且字面量等价」时转数字，
 * 雪花 id 保留字符串防丢精度；后端全局 AllowReadingFromString，两种都吃。
 */
export interface UserUpdateDto {
  nickName: string
  phone?: string | null
  email?: string | null
  deptId?: string | null
  status: number
  remark?: string | null
  roleIds?: (string | number)[]
  positionIds?: (string | number)[]
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
