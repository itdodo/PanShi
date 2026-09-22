import axios, {
  type AxiosError,
  type AxiosInstance,
  type AxiosRequestConfig,
  type AxiosResponse,
  type InternalAxiosRequestConfig
} from 'axios'
import { message } from '@/utils/feedback'
import { tokenStore } from '@/utils/token'
import { kickToLogin } from '@/utils/session'
import { BizError, type ApiResult } from './types'
import type { LoginResultDto } from './auth'

/**
 * 请求扩展项（axios 模块增强，业务侧写 `{ silent: true }` 直接通过类型检查）：
 * - raw：返回原始 AxiosResponse（读响应头 / 文件流），跳过统一拆包
 * - silent：不弹全局错误提示（未上线接口、后台轮询、调用方自行处理）
 * - anonymous：不注入 Bearer（登录 / 刷新 / 验证码）
 * - _retried：内部标记，401 刷新后重放一次，防死循环
 */
declare module 'axios' {
  interface AxiosRequestConfig {
    raw?: boolean
    silent?: boolean
    anonymous?: boolean
    _retried?: boolean
  }
}

export type RequestOptions = AxiosRequestConfig

export const API_BASE = '/api/v1'

const http: AxiosInstance = axios.create({
  baseURL: API_BASE,
  timeout: 20000
})

/* ------------------------------- 请求拦截 ------------------------------- */
http.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  if (!config.anonymous) {
    const token = tokenStore.access
    if (token) config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

/* --------------------- 401 单飞刷新（并发共享同一 Promise） -------------------- */
let refreshing: Promise<boolean> | null = null

async function doRefresh(): Promise<boolean> {
  const refreshToken = tokenStore.refresh
  if (!refreshToken) return false
  try {
    // 裸 axios：绕开拦截器，避免刷新自身 401 造成递归
    const resp = await axios.post<ApiResult<LoginResultDto>>(
      `${API_BASE}/auth/refresh`,
      { token: tokenStore.access, refreshToken },
      { timeout: 20000 }
    )
    const body = resp.data
    if (body?.code === 0 && body.data?.token) {
      tokenStore.set(body.data.token, body.data.refreshToken, body.data.mustChangePassword)
      window.dispatchEvent(new CustomEvent('ps:token-refreshed'))
      return true
    }
    return false
  } catch {
    return false
  }
}

function refreshOnce(): Promise<boolean> {
  refreshing ??= doRefresh().finally(() => {
    refreshing = null
  })
  return refreshing
}

/* ------------------------------- 响应拆包 ------------------------------- */
function unwrapResponse(resp: AxiosResponse): unknown {
  const cfg = resp.config
  const responseType = cfg.responseType ?? 'json'
  if (cfg.raw || responseType === 'blob' || responseType === 'arraybuffer') return resp

  const body: unknown = resp.data
  if (body === undefined || body === null || body === '') return null
  if (typeof body !== 'object' || !('code' in body)) return body // 非统一包（第三方/静态资源）原样返回
  const result = body as ApiResult
  if (result.code === 0) return result.data ?? null

  if (!cfg.silent) message.error(result.msg || '请求失败')
  return Promise.reject(new BizError(result.code, result.msg || '请求失败', resp.status))
}

async function handleResponseError(error: AxiosError<ApiResult>): Promise<unknown> {
  const cfg = error.config
  const resp = error.response
  const status = resp?.status
  const silent = cfg?.silent ?? false
  const url = cfg?.url ?? ''
  const msg = resp?.data?.msg

  if (axios.isCancel(error)) return Promise.reject(error)

  if (status === 401) {
    // 登录/验证码接口的 401 = 账号或密码错误，交给登录页提示，不做失效跳转
    const isLoginApi = url.includes('/auth/login') || url.includes('/auth/captcha')
    if (!isLoginApi) {
      if (cfg && !cfg._retried && tokenStore.refresh) {
        cfg._retried = true
        const ok = await refreshOnce()
        if (ok) {
          cfg.headers.set('Authorization', `Bearer ${tokenStore.access}`)
          return http.request(cfg)
        }
      }
      kickToLogin(msg || '登录状态已失效，请重新登录')
      return Promise.reject(new BizError(401, msg || '登录状态已失效，请重新登录', 401))
    }
  }

  if (!silent) {
    if (status === 403) message.error(msg || '没有权限执行此操作')
    else if (status === 409) message.warning(msg || '数据已被他人修改，请刷新后重试')
    else if (status === 400) message.error(msg || '请求参数不合法')
    else if (status === 429) message.warning(msg || '操作过于频繁，请稍后再试')
    else if (status && status >= 500) message.error(msg || '服务器开小差了，请稍后重试')
    else if (!status) message.error('网络异常，请确认后端服务已启动')
    else message.error(msg || `请求失败（${status}）`)
  }

  return Promise.reject(new BizError(status ?? -1, msg || error.message || '请求失败', status))
}

/**
 * axios 类型假定响应拦截器仍返回 AxiosResponse，而这里做的是「统一拆包」（resolve 出 data），
 * 故注册处集中一次断言；业务侧一律走下方 get/post/put/del 类型化封装。
 */
type ResponseFulfilled = (value: AxiosResponse) => AxiosResponse | Promise<AxiosResponse>
type ResponseRejected = (error: unknown) => never
http.interceptors.response.use(
  unwrapResponse as unknown as ResponseFulfilled,
  handleResponseError as unknown as ResponseRejected
)

/* ------------------------------ 类型化封装 ------------------------------ */

/** 统一入口：拦截器已拆包，resolve 值即 data */
export function request<T>(config: RequestOptions): Promise<T> {
  return http.request(config) as unknown as Promise<T>
}

/** 需要响应头（如 X-Captcha-Id）/ 文件流时使用 */
export function rawRequest<T = unknown>(config: RequestOptions): Promise<AxiosResponse<T>> {
  return http.request({ ...config, raw: true }) as unknown as Promise<AxiosResponse<T>>
}

export function get<T>(url: string, params?: object, config?: RequestOptions): Promise<T> {
  return request<T>({ ...config, method: 'GET', url, params })
}

export function post<T>(url: string, data?: unknown, config?: RequestOptions): Promise<T> {
  return request<T>({ ...config, method: 'POST', url, data })
}

export function put<T>(url: string, data?: unknown, config?: RequestOptions): Promise<T> {
  return request<T>({ ...config, method: 'PUT', url, data })
}

export function del<T>(url: string, config?: RequestOptions): Promise<T> {
  return request<T>({ ...config, method: 'DELETE', url })
}

/** 上传（multipart/form-data） */
export function upload<T>(url: string, file: File, extra?: Record<string, unknown>): Promise<T> {
  const form = new FormData()
  form.append('file', file)
  Object.entries(extra ?? {}).forEach(([k, v]) => {
    if (v !== undefined && v !== null) form.append(k, String(v))
  })
  return request<T>({ method: 'POST', url, data: form, headers: { 'Content-Type': 'multipart/form-data' } })
}

/** 下载（blob → a[download]，同时返回原始 Blob） */
export async function download(url: string, params?: object, fallbackName = 'download'): Promise<Blob> {
  const resp = await rawRequest<Blob>({ method: 'GET', url, params, responseType: 'blob' })
  const disposition = String(resp.headers?.['content-disposition'] ?? '')
  const matched = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)
  const fileName = matched ? decodeURIComponent(matched[1]) : fallbackName
  const blobUrl = URL.createObjectURL(resp.data)
  const a = document.createElement('a')
  a.href = blobUrl
  a.download = fileName
  document.body.appendChild(a)
  a.click()
  a.remove()
  window.setTimeout(() => URL.revokeObjectURL(blobUrl), 2000)
  return resp.data
}

export { http }
export default http
