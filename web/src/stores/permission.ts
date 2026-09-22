import { computed, h, ref } from 'vue'
import { defineStore } from 'pinia'
import type { MenuOption } from 'naive-ui'
import type { RouteRecordRaw, Router } from 'vue-router'
import { getMyMenuTree, MENU_STATUS, MENU_TYPE, type MenuTreeNode } from '@/api/menu'
import AppIcon from '@/components/AppIcon.vue'
import { normalizeRoutePath, resolveView } from '@/router/views'

const STATUS_DISABLED = MENU_STATUS.Disabled

/**
 * 静态路由已占用的路径：菜单里若再出现同路径（如「个人中心 /profile」，visible=false）跳过生成，
 * 避免同一路径两条路由记录互相抢匹配。
 */
const RESERVED_PATHS = new Set(['/profile', '/login', '/403', '/404', '/force/change-password'])

/**
 * 目录/停用/按钮不显示；按钮型只作权限码载体。
 * ⚠️ EnableStatus：0=启用 1=停用（与后端枚举一致，别写反）。
 */
function isShowNode(node: MenuTreeNode): boolean {
  if (node.menuType === MENU_TYPE.Button) return false
  if (node.visible === false) return false
  if (node.status === STATUS_DISABLED) return false
  return true
}

/** 递归生成 NMenu options（目录 → submenu，菜单 → 以路径为 key 的叶子） */
function toMenuOptions(nodes: MenuTreeNode[]): MenuOption[] {
  return [...nodes]
    .filter(isShowNode)
    .sort((a, b) => (a.sort ?? 0) - (b.sort ?? 0))
    .map((node) => {
      const children = (node.children ?? []).filter(isShowNode)
      const path = normalizeRoutePath(node.path ?? '')
      const option: MenuOption = {
        label: node.menuName,
        key: node.menuType === MENU_TYPE.Menu && path ? path : `menu:${node.id}`,
        icon: () => h(AppIcon, { name: node.icon, size: 17 })
      }
      if (children.length > 0) option.children = toMenuOptions(children)
      return option
    })
}

/** 路由 name：/system/user → system-user（同名冲突时追加菜单 id） */
function routeNameOf(node: MenuTreeNode, path: string): string {
  const base = path.replace(/^\/+/, '').replace(/[^\w]+/g, '-').replace(/^-+|-+$/g, '') || `menu-${node.id}`
  return /^\d/.test(base) ? `m-${base}` : base
}

function collectPages(nodes: MenuTreeNode[], out: MenuTreeNode[] = []): MenuTreeNode[] {
  for (const node of nodes) {
    if (!isShowNode(node)) continue
    if (node.menuType === MENU_TYPE.Menu && node.path) out.push(node)
    if (node.children?.length) collectPages(node.children, out)
  }
  return out
}

/**
 * 菜单树 → 动态路由表 + 侧栏菜单。
 * 仅 menuType=2 注册路由（挂到布局路由 'Layout' 下，path 为绝对路径）。
 */
export const usePermissionStore = defineStore('permission', () => {
  const rawMenus = ref<MenuTreeNode[]>([])
  const accessRoutes = ref<RouteRecordRaw[]>([])
  const loaded = ref(false)
  /** 菜单接口失败（后端未上线/无权限）：不白屏，守卫据此跳 403 引导页 */
  const loadFailed = ref(false)
  const removers: Array<() => void> = []

  const menuOptions = computed<MenuOption[]>(() => toMenuOptions(rawMenus.value))
  const homePath = computed<string>(() => accessRoutes.value[0]?.path ?? '/profile')
  const hasAnyMenu = computed(() => accessRoutes.value.length > 0)

  async function loadMenus(force = false): Promise<void> {
    if (loaded.value && !force) return
    try {
      const tree = await getMyMenuTree()
      rawMenus.value = Array.isArray(tree) ? tree : []
      loadFailed.value = false
    } catch (err) {
      rawMenus.value = []
      loadFailed.value = true
      console.warn('[permission] 菜单树加载失败（后端未上线时属预期），按无权限兜底', err)
    }
  }

  function buildRoutes(): RouteRecordRaw[] {
    const routes: RouteRecordRaw[] = []
    const used = new Set<string>()
    for (const node of collectPages(rawMenus.value)) {
      const path = normalizeRoutePath(node.path ?? '')
      if (!path || used.has(path) || RESERVED_PATHS.has(path)) continue
      used.add(path)
      const name = routeNameOf(node, path)
      routes.push({
        path,
        name,
        component: resolveView(name, node.component),
        meta: {
          title: node.menuName,
          icon: node.icon ?? '',
          keepAlive: true,
          cachedName: name,
          permission: node.permission ?? '',
          menuId: node.id
        }
      })
    }
    return routes
  }

  /** 注册到 router（幂等：重复调用先卸载旧的） */
  function register(router: Router): RouteRecordRaw[] {
    removers.splice(0).forEach((fn) => fn())
    const routes = buildRoutes()
    accessRoutes.value = routes
    routes.forEach((record) => {
      if (!router.hasRoute(record.name as string)) removers.push(router.addRoute('Layout', record))
    })
    loaded.value = true
    return routes
  }

  function findMenuByPath(path: string): MenuTreeNode | undefined {
    const walk = (nodes: MenuTreeNode[]): MenuTreeNode | undefined => {
      for (const n of nodes) {
        if (n.menuType === MENU_TYPE.Menu && normalizeRoutePath(n.path ?? '') === path) return n
        const hit = n.children?.length ? walk(n.children) : undefined
        if (hit) return hit
      }
      return undefined
    }
    return walk(rawMenus.value)
  }

  /** 面包屑链：目录 → … → 菜单（如 系统管理 / 用户管理） */
  function findMenuTrail(path: string): MenuTreeNode[] {
    const walk = (nodes: MenuTreeNode[], trail: MenuTreeNode[]): MenuTreeNode[] | null => {
      for (const n of nodes) {
        if (!isShowNode(n)) continue
        const next = [...trail, n]
        if (n.menuType === MENU_TYPE.Menu && normalizeRoutePath(n.path ?? '') === path) return next
        if (n.children?.length) {
          const hit = walk(n.children, next)
          if (hit) return hit
        }
      }
      return null
    }
    return walk(rawMenus.value, []) ?? []
  }

  function reset(): void {
    removers.splice(0).forEach((fn) => fn())
    rawMenus.value = []
    accessRoutes.value = []
    loaded.value = false
    loadFailed.value = false
  }

  return {
    rawMenus,
    accessRoutes,
    menuOptions,
    loaded,
    loadFailed,
    homePath,
    hasAnyMenu,
    loadMenus,
    register,
    buildRoutes,
    findMenuByPath,
    findMenuTrail,
    reset
  }
})
