import { del, get, post, put } from './http'
import type { VoidResult } from './types'

/** 菜单类型：1 目录 / 2 菜单（注册路由）/ 3 按钮（仅作权限码载体） */
export const MENU_TYPE = {
  Directory: 1,
  Menu: 2,
  Button: 3
} as const

export type MenuTypeValue = (typeof MENU_TYPE)[keyof typeof MENU_TYPE]

/** ⚠️ EnableStatus 与后端枚举一致：0=启用 1=停用（写反=整个菜单树消失） */
export const MENU_STATUS = {
  Enabled: 0,
  Disabled: 1
} as const

/** GET /sys/menu/tree/my 节点（后端批次 #6 并行开发，按契约写死类型） */
export interface MenuTreeNode {
  id: string
  parentId: string | null
  menuName: string
  menuType: MenuTypeValue
  /** 路由路径，如 /system/user */
  path?: string | null
  /** 组件相对路径，如 system/user/index → views/system/user/index.vue */
  component?: string | null
  /** 权限码，如 sys:user:list */
  permission?: string | null
  /** iconify 图标名，如 lucide:users（未知图标由 utils/menuIcon 兜底） */
  icon?: string | null
  visible: boolean
  /** 启用状态：0 启用 / 1 停用（见 MENU_STATUS，别写反） */
  status: number
  sort: number
  /** 乐观锁版本：后端 MenuDto 会回传（MenuService.ToDto），编辑提交必须原样带上 */
  version?: number
  children?: MenuTreeNode[] | null
}

/** 我的菜单树（登录即可，无需权限码；无角色 → 空数组） */
export function getMyMenuTree(): Promise<MenuTreeNode[]> {
  return get<MenuTreeNode[]>('/sys/menu/tree/my')
}

/* ---- 以下为菜单管理契约预声明（批次 #12 页面使用；后端未上线时 404 静默由调用方决定） ---- */

export interface MenuFormDto {
  id?: string
  parentId: string | null
  menuName: string
  menuType: MenuTypeValue
  path?: string | null
  component?: string | null
  permission?: string | null
  icon?: string | null
  visible: boolean
  status: number
  sort: number
  version?: number
}

/** 全量菜单树（权限码 sys:menu:list） */
export function getFullMenuTree(): Promise<MenuTreeNode[]> {
  return get<MenuTreeNode[]>('/sys/menu/tree')
}

/**
 * 授权用全量菜单树：登录即可、只读。角色「授权菜单」弹窗用这个，
 * 免得只管用户的角色（有 sys:role:* 却没 sys:menu:list）打开弹窗是 403 + 空树。
 */
export function getGrantMenuTree(): Promise<MenuTreeNode[]> {
  return get<MenuTreeNode[]>('/sys/menu/tree/grant')
}

export function createMenu(dto: MenuFormDto): Promise<string> {
  return post<string>('/sys/menu', dto)
}

/** 后端是 [HttpPut("{id:long}")]，路径必须带 id（曾漏掉写成裸 /sys/menu → 404） */
export function updateMenu(dto: MenuFormDto & { id: string }): Promise<VoidResult> {
  return put<VoidResult>(`/sys/menu/${dto.id}`, dto)
}

export function deleteMenu(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/menu/${id}`)
}
