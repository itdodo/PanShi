import { del, get, post, put, rawRequest, upload } from './http'

/* ------------------------------ 契约类型（已实测） ------------------------------ */

export interface LoginDto {
  userName: string
  password: string
  /** 验证码会话 Id（sys.captcha.enabled=0 时后端容忍空值，照发即可） */
  captchaId?: string
  captchaCode?: string
}

export interface LoginResultDto {
  token: string
  refreshToken: string
  /** 秒 */
  expiresIn: number
  /** 密码超期/被重置 → 前端强制改密 */
  mustChangePassword: boolean
}

export interface ProfileDto {
  id: string
  userName: string
  nickName: string
  phone?: string | null
  email?: string | null
  deptName?: string | null
  roles: string[]
  permissions: string[]
  /** 内置管理员：permissions 已含全量权限码，前端无需特判 */
  isAdmin: boolean
  pwdUpdateTime: string
}

export interface UpdateProfileDto {
  nickName: string
  phone?: string | null
  email?: string | null
  avatarFileId?: string | null
  /** 乐观锁：ProfileDto 暂未回传 version，留空=服务端跳过校验（后端补齐后请回传） */
  version?: number | null
}

export interface ChangePasswordDto {
  oldPassword: string
  newPassword: string
}

export interface SessionDto {
  id: string
  userId: string
  userName?: string | null
  loginIp?: string | null
  userAgent?: string | null
  createTime: string
  expireTime: string
  /** 是否当前会话（当前会话禁止下线） */
  current: boolean
}

/* --------------------------------- 接口 --------------------------------- */

export function login(dto: LoginDto): Promise<LoginResultDto> {
  return post<LoginResultDto>('/auth/login', dto, { anonymous: true })
}

export function logout(): Promise<void> {
  return post<void>('/auth/logout')
}

export function getProfile(): Promise<ProfileDto> {
  return get<ProfileDto>('/auth/profile')
}

export function updateProfile(dto: UpdateProfileDto): Promise<void> {
  return put<void>('/auth/profile', dto)
}

export function changePassword(dto: ChangePasswordDto): Promise<void> {
  return post<void>('/auth/change-password', dto)
}

/** 头像上传（白名单 + magic bytes 嗅探，2MB），返回 sys_file Id（字符串） */
export function uploadAvatar(file: File): Promise<string> {
  return upload<string>('/auth/avatar', file)
}

export function listSessions(): Promise<SessionDto[]> {
  return get<SessionDto[]>('/auth/sessions')
}

/** 下线自己的某个会话 */
export function kickSession(id: string): Promise<void> {
  return del<void>(`/auth/sessions/${id}`)
}

/**
 * 图形验证码：image/gif 字节流 + 响应头 X-Captcha-Id（后端 WithExposedHeaders 已配）。
 * 返回 objectUrl 供 <img :src>，调用方负责 URL.revokeObjectURL 释放。
 */
export async function getCaptcha(): Promise<{ objectUrl: string; captchaId: string }> {
  const resp = await rawRequest<Blob>({
    method: 'GET',
    url: '/auth/captcha',
    anonymous: true,
    responseType: 'blob',
    silent: true
  })
  const captchaId = String(resp.headers?.['x-captcha-id'] ?? '')
  return { objectUrl: URL.createObjectURL(resp.data), captchaId }
}
