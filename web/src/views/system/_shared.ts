import type { TreeOption } from 'naive-ui'

/**
 * 系统管理域（批次 #12）共用小板子：枚举映射 + 雪花 id 归一 + 树节点转换。
 * 只服务 src/views/system/** 下的页面，不向外扩散。
 * 枚举值一律对齐后端 Panshi.Model/Enums（数字序列化，非字符串）。
 */

export type NumOption = { label: string; value: number }
export type TagType = 'default' | 'primary' | 'info' | 'success' | 'warning' | 'error'

/* ------------------------------ 通用启用状态 ------------------------------ */

/** EnableStatus：0 正常 / 1 停用（⚠️ 写反 = 整棵菜单树消失，见 api/menu.ts MENU_STATUS） */
export const STATUS_OPTIONS: NumOption[] = [
  { label: '正常', value: 0 },
  { label: '停用', value: 1 }
]

export function statusTag(status?: number | null): { label: string; type: TagType } {
  return status === 1 ? { label: '停用', type: 'error' } : { label: '正常', type: 'success' }
}

/* -------------------------------- 数据权限 -------------------------------- */

/** DataScopeType 五档：1 全部 2 本部门 3 本部门及以下 4 仅本人 5 自定义 */
export const SCOPE_ALL = 1
export const SCOPE_CUSTOM = 5
export const SCOPE_OPTIONS: NumOption[] = [
  { label: '全部数据', value: 1 },
  { label: '本部门数据', value: 2 },
  { label: '本部门及以下', value: 3 },
  { label: '仅本人数据', value: 4 },
  { label: '自定义数据', value: 5 }
]

export function scopeLabel(value?: number | null): string {
  return SCOPE_OPTIONS.find((item) => item.value === value)?.label ?? '未设置'
}

/* ---------------------------------- 菜单 ---------------------------------- */

/** MenuType：1 目录 / 2 菜单（注册路由）/ 3 按钮（权限码载体） */
export const MENU_TYPE = { Directory: 1, Menu: 2, Button: 3 } as const
export const MENU_TYPE_OPTIONS: NumOption[] = [
  { label: '目录', value: MENU_TYPE.Directory },
  { label: '菜单', value: MENU_TYPE.Menu },
  { label: '按钮', value: MENU_TYPE.Button }
]

export function menuTypeTag(value?: number | null): { label: string; type: TagType } {
  if (value === MENU_TYPE.Directory) return { label: '目录', type: 'info' }
  if (value === MENU_TYPE.Button) return { label: '按钮', type: 'warning' }
  return { label: '菜单', type: 'primary' }
}

/* ---------------------------------- 公告 ---------------------------------- */

/** NoticeType：1 通知 / 2 公告 */
export const NOTICE_TYPE_OPTIONS: NumOption[] = [
  { label: '通知', value: 1 },
  { label: '公告', value: 2 }
]

/**
 * NoticeStatus：0 停用/草稿 / 1 已发布 / 2 定时发布（到期由 sys.notice.publish 作业置 1）。
 * 0 态按「是否曾有过发布时间」区分文案：无 publishTime = 从未发布的草稿，有 = 发布后又停用。
 */
export const NOTICE_STATUS = { Stopped: 0, Published: 1, Scheduled: 2 } as const
export const NOTICE_STATUS_OPTIONS: NumOption[] = [
  { label: '停用', value: NOTICE_STATUS.Stopped },
  { label: '已发布', value: NOTICE_STATUS.Published },
  { label: '定时发布', value: NOTICE_STATUS.Scheduled }
]

export function noticeTypeLabel(value?: number | null): string {
  return NOTICE_TYPE_OPTIONS.find((item) => item.value === value)?.label ?? '通知'
}

export function noticeStatusTag(value?: number | null, publishTime?: string | null): { label: string; type: TagType } {
  if (value === NOTICE_STATUS.Published) return { label: '已发布', type: 'success' }
  if (value === NOTICE_STATUS.Scheduled) return { label: '定时', type: 'info' }
  return publishTime ? { label: '停用', type: 'default' } : { label: '草稿', type: 'default' }
}

/* -------------------------------- 字典标签色 -------------------------------- */

/** DictData.tagType 直接存 Naive tag type 字符串 */
export const TAG_TYPE_OPTIONS: { label: string; value: string }[] = [
  { label: '默认 default', value: 'default' },
  { label: '信息 info', value: 'info' },
  { label: '成功 success', value: 'success' },
  { label: '警告 warning', value: 'warning' },
  { label: '危险 error', value: 'error' }
]

export function tagTypeOf(value?: string | null): TagType {
  // 字典种子数据里写的是 "danger"（DbSeeder），而 Naive 的对应色叫 "error"；不别名会一律退成默认灰
  if (value === 'danger') return 'error'
  const allowed: TagType[] = ['default', 'primary', 'info', 'success', 'warning', 'error']
  return allowed.includes(value as TagType) ? (value as TagType) : 'default'
}

/* --------------------------------- id 归一 --------------------------------- */

/**
 * 提交后端 List<long> / long 字段。
 * ⚠️ 雪花 ID 可达 19 位（> 2^53），纯 Number 化会静默丢精度 → 仅「安全整数且字面量等价」才转数字，
 * 否则保留字符串（后端 JsonConfig 全局 NumberHandling.AllowReadingFromString，数字/字符串都能绑定）。
 */
export function idForApi(id?: string | number | null): string | number | null {
  if (id === null || id === undefined || id === '') return null
  const num = Number(id)
  return Number.isSafeInteger(num) && String(num) === String(id) ? num : String(id)
}

export function toIds(ids?: readonly (string | number)[] | null): (string | number)[] {
  return (ids ?? [])
    .map((id) => idForApi(id))
    .filter((id): id is string | number => id !== null)
}

/** 多选控件回写：Naive 回调值类型宽 → 统一字符串数组（模型层一律 string，防精度丢失） */
export function toStrIds(value: unknown): string[] {
  if (Array.isArray(value)) return value.map((item) => String(item))
  if (value === null || value === undefined || value === '') return []
  return [String(value)]
}

/** 单选（NTreeSelect / NSelect）清空后给 null / '' / 数组 → 归一 string | null */
export function toId(value: unknown): string | null {
  const first = Array.isArray(value) ? value[0] : value
  if (first === null || first === undefined || first === '') return null
  return String(first)
}

/** 后端 total / 耗时等 long 字段兜底（拦截器已拆包，这里只做 Number 化） */
export function toNum(value: unknown, fallback = 0): number {
  const num = Number(value)
  return Number.isFinite(num) ? num : fallback
}

/* ---------------------------------- 树工具 ---------------------------------- */

/**
 * 后端树（DeptDto / MenuDto，children 为 [] 也要当叶子）→ Naive 树节点。
 * 返回 TreeOption：NTree 与 NTreeSelect 共用（两者结构同源 TreeOptionBase）。
 */
export function toTreeOptions<T>(
  nodes: readonly T[],
  keyOf: (node: T) => string,
  labelOf: (node: T) => string,
  childrenOf: (node: T) => readonly T[] | null | undefined
): TreeOption[] {
  return nodes.map((node) => {
    const option: TreeOption = { key: keyOf(node), label: labelOf(node) }
    const kids = childrenOf(node)
    if (kids && kids.length) option.children = toTreeOptions(kids, keyOf, labelOf, childrenOf)
    return option
  })
}

/** 递归去掉空 children（否则 NDataTable 树形给叶子画展开箭头） */
export function pruneChildren<T extends { children?: T[] | null }>(nodes: readonly T[]): T[] {
  return nodes.map((node) => {
    const copy = { ...node } as T
    const kids = node.children
    if (kids && kids.length) copy.children = pruneChildren(kids)
    else delete (copy as { children?: unknown }).children
    return copy
  })
}

/** 嵌套树拍平（菜单/部门「展开全部」、统计层级用） */
export function flattenTree<T>(
  nodes: readonly T[],
  childrenOf: (node: T) => readonly T[] | null | undefined
): T[] {
  const out: T[] = []
  const walk = (list: readonly T[]): void => {
    list.forEach((node) => {
      out.push(node)
      const kids = childrenOf(node)
      if (kids && kids.length) walk(kids)
    })
  }
  walk(nodes)
  return out
}

/** 扁平列表 → 嵌套树（后端个别接口只给平铺时用；parentId 根为 null/'0'） */
export function buildTree<T extends { id: string; parentId?: string | null }>(
  flat: readonly T[],
  childrenKey: 'children' = 'children'
): (T & { children?: T[] })[] {
  const nodes = flat.map((item) => ({ ...item }) as T & { children?: T[] })
  const byId = new Map(nodes.map((node) => [node.id, node]))
  const roots: (T & { children?: T[] })[] = []
  nodes.forEach((node) => {
    const parent = node.parentId && node.parentId !== '0' ? byId.get(node.parentId) : undefined
    if (parent) {
      parent[childrenKey] = parent[childrenKey] ?? []
      parent[childrenKey].push(node)
    } else {
      roots.push(node)
    }
  })
  return roots
}
