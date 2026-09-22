/** 令牌持久化（localStorage）：user store 与 http 拦截器共用同一份真源，避免循环依赖 */

const ACCESS_KEY = 'ps:access-token'
const REFRESH_KEY = 'ps:refresh-token'
const MUST_CHANGE_KEY = 'ps:must-change-password'

function read(key: string): string {
  try {
    return localStorage.getItem(key) ?? ''
  } catch {
    return ''
  }
}

export const tokenStore = {
  get access(): string {
    return read(ACCESS_KEY)
  },
  get refresh(): string {
    return read(REFRESH_KEY)
  },
  /** 密码超期/被重置标记（刷新令牌时后端也会回传，长开页签据此强制改密） */
  get mustChange(): boolean {
    return read(MUST_CHANGE_KEY) === '1'
  },
  set(accessToken: string, refreshToken?: string, mustChange?: boolean): void {
    try {
      localStorage.setItem(ACCESS_KEY, accessToken)
      if (refreshToken) localStorage.setItem(REFRESH_KEY, refreshToken)
      if (mustChange !== undefined) localStorage.setItem(MUST_CHANGE_KEY, mustChange ? '1' : '0')
    } catch {
      /* 隐私模式下忽略 */
    }
  },
  clear(): void {
    try {
      localStorage.removeItem(ACCESS_KEY)
      localStorage.removeItem(REFRESH_KEY)
      localStorage.removeItem(MUST_CHANGE_KEY)
    } catch {
      /* ignore */
    }
  }
}
