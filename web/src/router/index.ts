import { createRouter, createWebHistory } from 'vue-router'
import { usePermissionStore } from '@/stores/permission'
import { useTabsStore } from '@/stores/tabs'
import { useUserStore } from '@/stores/user'
import { catchAllRoute, isWhitePath, staticRoutes } from './routes'

export const FORCE_CHANGE_PWD_PATH = '/force/change-password'
export const APP_TITLE = '磐石管理系统'
/** 记录因 chunk 加载失败已自动重载的目标，防同一目标反复重载 */
const CHUNK_RELOAD_KEY = 'ps:chunk-reload'

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
  sessionStorage.removeItem(CHUNK_RELOAD_KEY)
  const tabs = useTabsStore()
  if (to.matched.some((r) => r.name === 'Layout')) {
    // 登录重定向到指定子页时首页不会自然入栈——先兜底把首页页签置顶，再入栈当前页
    const perm = usePermissionStore()
    if (perm.loaded) {
      // resolve() 运行时返回规范化位置，但类型为 RouteLocationResolvedGeneric（matched 偏松），故断言
      tabs.ensureHome(router.resolve(perm.homePath) as unknown as typeof to)
    }
    tabs.addTab(to)
  }
  const title = to.meta.title
  document.title = title ? `${title} · ${APP_TITLE}` : APP_TITLE
})

/**
 * 部署后旧会话仍引用被替换掉的哈希 chunk：懒加载 import() 失败 → 页面空白报错。
 * 捕获该失败并整页重载一次（拉取新入口 index.html + 新 chunk）；同一目标只重载一次，防死循环。
 */
const CHUNK_FAIL_RE =
  /dynamically imported module|importing a module script failed|failed to fetch dynamically|loading chunk \S+ failed|unable to preload css/i

router.onError((error, to) => {
  const msg = String((error as { message?: string })?.message ?? error)
  if (!CHUNK_FAIL_RE.test(msg)) return
  const target = to?.fullPath || window.location.pathname + window.location.search
  if (sessionStorage.getItem(CHUNK_RELOAD_KEY) === target) return
  sessionStorage.setItem(CHUNK_RELOAD_KEY, target)
  window.location.href = target
})

export default router
