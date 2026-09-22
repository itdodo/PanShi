import { message } from './feedback'
import { tokenStore } from './token'

/**
 * 回登录页的统一兜底：
 * - 提示后延迟 2 秒跳转（SignalR force-logout 与 401 并发场景的前端约定）
 * - 并发去重：多个请求同时 401 / 同时收到 force-logout 只跳一次
 */
let kicking = false

export function kickToLogin(reason?: string, delay = 2000): void {
  if (kicking) return
  kicking = true
  tokenStore.clear()
  message.warning(reason || '登录状态已失效，请重新登录', { duration: Math.max(delay - 200, 1500) })

  void (async () => {
    try {
      const [{ useUserStore }, { default: router }] = await Promise.all([
        import('@/stores/user'),
        import('@/router')
      ])
      useUserStore().resetAll()
      window.setTimeout(() => {
        const current = router.currentRoute.value
        const redirect = current.path === '/login' || current.path === '/' ? undefined : current.fullPath
        void router.replace({ path: '/login', query: redirect ? { redirect } : undefined }).then(() => {
          window.setTimeout(() => {
            kicking = false
          }, 1000)
        })
      }, delay)
    } catch (err) {
      kicking = false
      console.error('[session] 跳转登录页失败', err)
    }
  })()
}

/** 登录成功后复位兜底闩，保证后续仍能正常触发强踢 */
export function resetKickFlag(): void {
  kicking = false
}
