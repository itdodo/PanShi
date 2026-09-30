import { computed, h, ref } from 'vue'
import { NTag, type SelectOption } from 'naive-ui'
import { getDictItems, type DictItemDto } from '@/api/system/dict'

/**
 * 基础资料三页（物料/供应商/客户）共用的小板子。只服务 src/views/basedata/**。
 * 主数据的分类/单位/等级都存字典 value，所以列表要把 value 渲染回 label —— 字典项按类型取一次即可。
 */

/** EnableStatus：0 启用 / 1 停用（与系统管理域同义，这里独立一份避免跨域引用） */
export const MD_STATUS_OPTIONS: SelectOption[] = [
  { label: '启用', value: 0 },
  { label: '停用', value: 1 }
]

export function mdStatusTag(status?: number | null): { label: string; type: 'success' | 'default' } {
  return status === 1 ? { label: '停用', type: 'default' } : { label: '启用', type: 'success' }
}

export function renderStatusTag(status?: number | null) {
  const tag = mdStatusTag(status)
  return h(NTag, { size: 'small', bordered: false, type: tag.type }, { default: () => tag.label })
}

/** 字典项 → NSelect 选项 + value→label 映射（一次取回，列表渲染与表单下拉共用） */
export function useDict(code: string) {
  const items = ref<DictItemDto[]>([])
  const options = computed<SelectOption[]>(() => items.value.map((i) => ({ label: i.label, value: i.value })))
  const labelOf = (value?: string | null) =>
    value ? (items.value.find((i) => i.value === value)?.label ?? value) : ''

  async function load(): Promise<void> {
    try {
      items.value = await getDictItems(code)
    } catch {
      items.value = [] // getDictItems 是 silent 的，字典没配也不该挡住主数据页
    }
  }

  return { options, labelOf, load }
}

/** 金额列：null 显示「未定价」而不是 0 —— 两者含义不同 */
export function renderPrice(value?: number | null) {
  if (value === null || value === undefined) return h('span', { class: 'ps-muted' }, '未定价')
  return h('span', { style: 'font-variant-numeric: tabular-nums' }, value.toFixed(2))
}

/** 空字段统一灰化，避免列表里出现「看着有值其实是空格」 */
export function orMuted(value?: string | null) {
  return value ? value : h('span', { class: 'ps-muted' }, '-')
}
