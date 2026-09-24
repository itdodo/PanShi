import { computed, ref, watch } from 'vue'
import { defineStore } from 'pinia'
import type { RouteLocationNormalized } from 'vue-router'

const STORAGE_KEY = 'ps:tabs'
/** 首页兜底 path（菜单未加载时）；真实首页由 ensureHome 按 perm.homePath 归一 */
const DEFAULT_HOME_PATH = '/dashboard'

export interface TabItem {
  /** 路由 name */
  name: string
  /** 路由 path（页签唯一键） */
  path: string
  fullPath: string
  title: string
  icon?: string
  /** 首页等不可关闭 */
  closable: boolean
  keepAlive: boolean
  /** keep-alive include 值（= 包装组件 name） */
  cachedName?: string
}

interface StoreSnapshot {
  tabs: TabItem[]
  activePath: string
}

function restore(): StoreSnapshot {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return { tabs: [], activePath: '' }
    const parsed = JSON.parse(raw) as StoreSnapshot
    const tabs = Array.isArray(parsed.tabs)
      ? parsed.tabs
          .filter((t) => !!t && !!t.path && !!t.name)
          .map((t) => ({ ...t, closable: t.path !== DEFAULT_HOME_PATH })) // closable 初值按默认首页归一；ensureHome 再按真实首页修正
      : []
    return { tabs, activePath: typeof parsed.activePath === 'string' ? parsed.activePath : '' }
  } catch {
    return { tabs: [], activePath: '' }
  }
}

const initial = restore()

/**
 * 多页签：新增/关闭/关闭其他/关闭全部 + sessionStorage 持久化。
 * 「刷新当前页签」= 按 path 递增的渲染 key（⚠️ key 必须含 path，纯数字会跨组件撞缓存）。
 */
export const useTabsStore = defineStore('tabs', () => {
  const tabs = ref<TabItem[]>(initial.tabs)
  const activePath = ref(initial.activePath)
  /** 首页 path（closable 判定基准；ensureHome 会按 perm.homePath 归一到真实首页） */
  const homePath = ref(DEFAULT_HOME_PATH)
  /** path → 刷新次数（不持久化） */
  const refreshSeq = ref<Record<string, number>>({})

  const cachedNames = computed<string[]>(() => [
    ...new Set(tabs.value.filter((t) => t.keepAlive && t.cachedName).map((t) => t.cachedName as string))
  ])

  watch(
    [tabs, activePath],
    () => {
      try {
        sessionStorage.setItem(
          STORAGE_KEY,
          JSON.stringify({ tabs: tabs.value, activePath: activePath.value } satisfies StoreSnapshot)
        )
      } catch {
        /* 配额/隐私模式忽略 */
      }
    },
    { deep: true }
  )

  /** keep-alive + :key —— 含 path 保证不同页签互不撞缓存 */
  function componentKey(path: string): string {
    return `${path}#${refreshSeq.value[path] ?? 0}`
  }

  function touch(path: string): void {
    activePath.value = path
  }

  /** 由路由守卫/布局在导航后调用：命中布局内的页面才生成页签 */
  function toItem(route: RouteLocationNormalized): TabItem | null {
    if (route.meta.hideTabs) return null
    if (!route.matched.some((r) => r.name === 'Layout')) return null
    const title = route.meta.title
    if (!title) return null
    return {
      name: String(route.name ?? route.path),
      path: route.path,
      fullPath: route.fullPath,
      title,
      icon: route.meta.icon,
      // 首页不可关闭，其余一律可关（修复：深链/刷新使非首页成为「首个」时被误锁）
      closable: route.path !== homePath.value,
      keepAlive: route.meta.keepAlive !== false,
      cachedName: route.meta.cachedName ?? String(route.name ?? '')
    }
  }

  /** 入栈：已存在则就地更新，否则插到末尾/首位；atFront=true 时把已存在的挪到第一位 */
  function upsert(item: TabItem, atFront: boolean): void {
    const idx = tabs.value.findIndex((t) => t.path === item.path)
    if (idx >= 0) {
      const hit = tabs.value[idx]
      hit.fullPath = item.fullPath
      hit.title = item.title
      hit.closable = item.closable
      if (atFront && idx !== 0) {
        tabs.value.splice(idx, 1)
        tabs.value.unshift(hit)
      }
      return
    }
    if (atFront) tabs.value.unshift(item)
    else tabs.value.push(item)
  }

  function addTab(route: RouteLocationNormalized): void {
    const item = toItem(route)
    if (!item) return
    upsert(item, false)
    touch(item.path)
  }

  /**
   * 保证首页页签存在、置顶且不可关闭；不改动当前激活页签。
   * 登录重定向到指定子页时，首页不会自然入栈——在 addTab(to) 前调用本方法兜底。
   */
  function ensureHome(route: RouteLocationNormalized): void {
    if (!route.matched.some((r) => r.name === 'Layout')) return
    homePath.value = route.path
    const item = toItem(route)
    if (!item) return
    item.closable = false
    upsert(item, true)
  }

  function updateFullPath(path: string, fullPath: string): void {
    const hit = tabs.value.find((t) => t.path === path)
    if (hit) hit.fullPath = fullPath
    touch(path)
  }

  /** @returns 需要跳转的地址（fullPath）；null = 停留在当前页 */
  function close(path: string): string | null {
    const index = tabs.value.findIndex((t) => t.path === path)
    if (index < 0) return null
    const target = tabs.value[index]
    if (!target.closable) return null
    tabs.value.splice(index, 1)
    if (activePath.value !== path) return null
    const next = tabs.value[Math.min(index, tabs.value.length - 1)]
    return next ? next.fullPath || next.path : '/'
  }

  /** 关闭其他：保留不可关闭页签（首页）+ 目标页签，并激活目标页签 */
  function closeOthers(path: string): string | null {
    if (!tabs.value.some((t) => t.path === path)) return null
    tabs.value = tabs.value.filter((t) => t.path === path || !t.closable)
    touch(path)
    return null
  }

  /** 关闭全部：保留不可关闭页签（首页），返回应跳转的地址 */
  function closeAll(): string {
    tabs.value = tabs.value.filter((t) => !t.closable)
    const first = tabs.value[0]
    return first ? first.fullPath || first.path : '/'
  }

  /** 刷新当前页签：递增该 path 的渲染 key（key 含 path） */
  function refresh(path: string): void {
    refreshSeq.value[path] = (refreshSeq.value[path] ?? 0) + 1
  }

  function reset(): void {
    tabs.value = []
    activePath.value = ''
    refreshSeq.value = {}
    try {
      sessionStorage.removeItem(STORAGE_KEY)
    } catch {
      /* ignore */
    }
  }

  return {
    tabs,
    activePath,
    homePath,
    cachedNames,
    refreshSeq,
    componentKey,
    addTab,
    ensureHome,
    updateFullPath,
    close,
    closeOthers,
    closeAll,
    refresh,
    touch,
    reset
  }
})
