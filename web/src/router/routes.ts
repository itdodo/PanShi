import type { RouteRecordRaw } from 'vue-router'
import { LayoutView, namedView } from './views'

/** 布局内需要常驻的静态路由（不依赖菜单接口） */
export const staticRoutes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('@/views/login/index.vue'),
    meta: { title: '登录', public: true }
  },
  {
    path: '/403',
    name: 'Forbidden',
    component: () => import('@/views/403.vue'),
    meta: { title: '无访问权限', public: true }
  },
  {
    path: '/404',
    name: 'NotFound',
    component: () => import('@/views/404.vue'),
    meta: { title: '页面不存在', public: true }
  },
  {
    path: '/force/change-password',
    name: 'ForceChangePassword',
    component: () => import('@/views/common/ForceChangePassword.vue'),
    meta: { title: '修改密码', public: false, hideTabs: true }
  },
  {
    path: '/',
    name: 'Layout',
    component: LayoutView,
    // 首页目标由守卫在动态路由生成后决定（此处不能写 redirect：
    // 重定向在守卫之前解析，那时菜单还没加载，会把所有人扔到 /profile）
    meta: { public: false },
    children: [
      {
        path: '/profile',
        name: 'Profile',
        component: namedView('Profile', () => import('@/views/system/profile/index.vue')),
        meta: {
          title: '个人中心',
          icon: 'lucide:user-round',
          keepAlive: true,
          cachedName: 'Profile',
          hideInMenu: true
        }
      }
    ]
  }
]

/**
 * 通配兜底：动态路由生成后仍能命中具体页面（通配权重最低）。
 * ⚠️ 不能用 redirect:'/404'——重定向在守卫之前解析，会丢失原始 path，
 * 导致整页刷新任意深链时守卫拿到的 to.path 已是 /404、无法重新解析到动态路由。
 * 改为直接渲染 404 组件：守卫据 to.name==='NotFoundCatch' 判定真 404。
 */
export const catchAllRoute: RouteRecordRaw = {
  path: '/:pathMatch(.*)*',
  name: 'NotFoundCatch',
  component: () => import('@/views/404.vue'),
  meta: { title: '页面不存在', public: true }
}

/** 守卫白名单（无需登录） */
export const WHITE_LIST = ['/login', '/403', '/404']

export function isWhitePath(path: string): boolean {
  return WHITE_LIST.includes(path)
}
