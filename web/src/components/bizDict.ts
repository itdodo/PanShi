import { getDictItems } from '@/api/system/dict'

/**
 * 业务样板字典（报销类别）：优先后端 sys_expense_category，失败/为空回落硬编码（种子值一致）。
 */
export const EXPENSE_CATEGORY_DICT = 'sys_expense_category'

export interface CategoryOption {
  value: string
  label: string
}

export const EXPENSE_CATEGORIES: CategoryOption[] = [
  { value: 'travel', label: '差旅' },
  { value: 'office', label: '办公用品' },
  { value: 'hospitality', label: '业务招待' },
  { value: 'telecom', label: '通讯' },
  { value: 'other', label: '其他' }
]

let cache: CategoryOption[] | null = null

/** 只拉一次（字典接口静默失败，回落硬编码不阻塞表单） */
export async function loadExpenseCategories(): Promise<CategoryOption[]> {
  if (cache) return cache
  try {
    const items = await getDictItems(EXPENSE_CATEGORY_DICT)
    cache = items?.length ? items.map((i) => ({ value: i.dictValue, label: i.dictLabel })) : EXPENSE_CATEGORIES
  } catch {
    cache = EXPENSE_CATEGORIES
  }
  return cache
}

export function categoryLabel(code?: string | null, list: CategoryOption[] = EXPENSE_CATEGORIES): string {
  if (!code) return '-'
  return list.find((c) => c.value === code)?.label ?? code
}
