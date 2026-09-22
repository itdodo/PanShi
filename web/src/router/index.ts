import { createRouter, createWebHistory } from 'vue-router'
import { usePermissionStore } from '@/stores/permission'
import { useTabsStore } from '@/stores/tabs'
import { useUserStore } from '@/stores/user'
import { catchAllRoute, isWhitePath, staticRoutes } from './routes'

export const FORCE_CHANGE_PWD_PATH = '/force/change-password'
export const APP_TITLE = '磐石管理底座'

const router = createRouter({
  history: createWebHistory(),
  routes: [...staticRoutes, catchAllRoute],
  scrollBehavior: () => ({ left: 0, top: 0 })
})

router.beforeEach(async (to) => {
  const user = useUserStore()
  const perm = usePermissionStore()

  /* 未登录 */
  if (!user.token) {
    if (isWhitePath(to.path) || to.meta.public) return true
    return { path: '/login', query: to.fullPath === '/' ? undefined : { redirect: to.fullPath } }
  }

  /* 已登录还去登录页 → 回首页 */
  if (to.path === '/login') return { path: perm.loaded ? perm.homePath : '/' }

  /* profile 未加载：拉 profile + 生成动态路由（addRoute 后整页内重新匹配一次） */
  if (!user.loaded) {
    try {
      await user.loadProfile()
    } catch {
      user.resetAll()
      return { path: '/login', query: to.fullPath === '/' ? undefined : { redirect: to.fullPath } }
    }
  }

  /* 密码超期/被重置 → 强制改密（不依赖菜单，避免改密页因无菜单被拦） */
  if (user.mustChangePassword) {
    return to.path === FORCE_CHANGE_PWD_PATH ? true : { path: FORCE_CHANGE_PWD_PATH, replace: true }
  }

  if (!perm.loaded) {
    await perm.loadMenus()
    perm.register(router)

    /* 无角色 / 菜单接口失败 → 403 友好页（不得白屏报错） */
    if (!perm.hasAnyMenu) {
      if (to.path === '/403') return true
      return {
        path: '/403',
        query: {
          reason: perm.loadFailed ? '菜单接口暂不可用，请稍后重试' : '当前账号未分配任何菜单，请联系管理员'
        }
      }
    }
    /* 路由表刚注入：重新解析目标（'/' 落到第一个可访问菜单）。
       ⚠️ 此处不能提前用 to.name==='NotFoundCatch' 判 404——首解析时动态路由尚未注册，
       任何深链都会先命中 catch-all；必须重新解析后再判（真 404 由下方已加载分支兜底）。 */
    return {
      path: to.path === '/' ? perm.homePath : to.path,
      query: to.query,
      hash: to.hash,
      replace: true
    }
  }

  /* 首页 = 菜单树里第一个可访问页面 */
  if (to.path === '/') return { path: perm.homePath, replace: true }

  /* 已生成路由后仍命中通配 = 真 404（redirect 到 /404） */
  if (to.name === 'NotFoundCatch') return true

  const required = to.meta.permission
  if (required && !user.hasPermission(required)) {
    return { path: '/403', query: { reason: `缺少访问权限：${required}` } }
  }
  return true
})

router.afterEach((to) => {
  const tabs = useTabsStore()
  if (to.matched.some((r) => r.name === 'Layout')) tabs.addTab(to)
  const title = to.meta.title
  document.title = title ? `${title} · ${APP_TITLE}` : APP_TITLE
})

export default router
