import { ref } from 'vue'
import { addCollection } from '@iconify/vue/offline'
// ⚠️ 静态导入 lucide 集合：生产构建下动态 import('*.json') 会被 Rollup 生成损坏的 interop
// （`e is not a function`，导致 icons chunk 顶层求值失败、整个 app 不挂载）。
// 该 chunk 本就随入口静态加载，动态导入并不能真正懒加载，故直接静态导入最稳。
import lucideCollection from '@iconify-json/lucide/icons.json'

/**
 * 菜单图标解析：后端 menu.icon 存 iconify 字符串（如 `lucide:users`），
 * 运行时把 @iconify-json/lucide 的集合 addCollection 进离线版 @iconify/vue，
 * 未知/缺失图标兜底「文档」图标，绝不空白渲染或抛错（蓝图红线 #18）。
 *
 * 用 `@iconify/vue/offline` 而非默认入口：内网部署不发任何 Iconify API 请求。
 * ⚠️ lucide 无 `document` 名（旧名已并入 file-text），兜底必须用 file-text。
 */

export const FALLBACK_ICON = 'lucide:file-text'
const DEFAULT_PREFIX = 'lucide'

const known = new Set<string>()
/** 集合就绪标记：让 resolveMenuIcon 具备响应性，加载完成后图标自动从兜底切换为真图标 */
const ready = ref(false)
let loading: Promise<void> | null = null

function loadLucide(): void {
  try {
    const collection = lucideCollection as unknown as {
      prefix?: string
      width?: number
      height?: number
      icons?: Record<string, unknown>
      aliases?: Record<string, unknown>
    }
    const icons = collection.icons ?? {}
    addCollection({
      prefix: collection.prefix || DEFAULT_PREFIX,
      icons: icons as never,
      aliases: collection.aliases as never,
      width: collection.width,
      height: collection.height
    })
    Object.keys(icons).forEach((name) => known.add(`${DEFAULT_PREFIX}:${name}`))
    Object.keys(collection.aliases ?? {}).forEach((name) => known.add(`${DEFAULT_PREFIX}:${name}`))
    ready.value = true
  } catch (err) {
    console.warn('[menuIcon] lucide 图标集合加载失败，全部使用兜底图标', err)
  }
}

/** 幂等预热（AppIcon 组件 setup 调用） */
export function ensureIconCollection(): Promise<void> {
  loading ??= Promise.resolve().then(() => loadLucide())
  return loading
}

/** `users` / `lucide:users` / `i-lucide-users` → `lucide:users` */
export function normalizeIconName(raw?: string | null): string {
  const trimmed = (raw ?? '').trim()
  if (!trimmed) return ''
  const colon = trimmed.indexOf(':')
  if (colon > 0) return trimmed.toLowerCase()
  if (trimmed.startsWith('i-')) return `${DEFAULT_PREFIX}:${trimmed.slice(2)}`.toLowerCase()
  return `${DEFAULT_PREFIX}:${trimmed}`.toLowerCase()
}

/** 解析成 Icon 组件可直接渲染的名字；集合中不存在（含尚未加载完）则兜底 */
export function resolveMenuIcon(raw?: string | null): string {
  void ready.value // 依赖收集：集合就绪后重新计算
  const name = normalizeIconName(raw)
  if (!name) return FALLBACK_ICON
  return known.has(name) ? name : FALLBACK_ICON
}

/**
 * 当前可渲染的图标名全集（`lucide:xxx`，已排序）。
 * 集合是异步装载的，调用方要放在 computed/响应式上下文里读，才会从空数组变成有内容。
 */
export function listIconNames(): string[] {
  void ready.value
  return [...known].sort()
}
