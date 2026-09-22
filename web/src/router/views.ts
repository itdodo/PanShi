import { defineAsyncComponent, defineComponent, h, type Component } from 'vue'

/**
 * 视图解析：菜单 component 字符串（如 `system/user/index`）→ `src/views/system/user/index.vue`。
 * 找不到组件不白屏，兜底渲染「页面开发中」。
 *
 * ⚠️ keep-alive 的 include 匹配的是**组件自身 name**，不是路由 name；
 * 因此这里用 namedView() 包一层，保证组件 name === 路由 name === 页签 cachedName。
 */
const viewModules = import.meta.glob('../views/**/*.vue')

type Loader = () => Promise<unknown>

export function namedView(name: string, loader: Loader): Component {
  const inner = defineAsyncComponent(loader as () => Promise<Component>)
  return defineComponent({ name, render: () => h(inner) })
}

function moduleKey(componentPath: string): string {
  const cleaned = componentPath.trim().replace(/^\/+/, '').replace(/\.vue$/, '')
  return `../views/${cleaned}.vue`
}

export function resolveView(routeName: string, componentPath?: string | null): Component {
  const path = (componentPath ?? '').trim()
  const loader = path ? viewModules[moduleKey(path)] : undefined
  if (!loader) {
    console.warn(`[router] 菜单 component「${path || '空'}」未匹配到 views 文件，使用占位页`)
    return namedView(routeName, () => import('@/views/common/Developing.vue'))
  }
  return namedView(routeName, loader)
}

/** 主布局（懒加载，内部含 NConfigProvider 之外的 provider 已在 App.vue） */
export const LayoutView = namedView('LayoutRoot', () => import('@/layouts/default/index.vue'))

/** 路径归一：菜单里配的 /system/user 直接作为布局子路由（vue-router 支持子路由绝对路径） */
export function normalizeRoutePath(raw: string): string {
  const path = raw.trim()
  if (!path) return ''
  return path.startsWith('/') ? path : `/${path}`
}
