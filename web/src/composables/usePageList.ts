import { computed, reactive, ref, type ComputedRef, type Ref } from 'vue'
import type { PaginationProps } from 'naive-ui'
import { errorText, type PageQuery, type PagedResult, type SortOrder } from '@/api/types'

export interface PageListOptions<T, Q> {
  /** 分页接口：入参 = 查询条件 + pageNum/pageSize/sortField/sortOrder */
  fetcher: (query: Q & PageQuery) => Promise<PagedResult<T>>
  /** 查询条件初值工厂（reset 时回到这里） */
  defaultQuery?: () => Q
  pageSize?: number
  immediate?: boolean
  onLoaded?: (rows: T[], total: number) => void
}

export interface PageSorter {
  columnKey?: string | number | null
  /**
   * ⚠️ NDataTable 表头给的是 'ascend' | 'descend' | false，不是后端的 'asc'/'desc'。
   * 直接透传会让 ResolveSort 的 `desc = sortOrder != "asc"` 恒为真 —— 所有升序都退化成降序。
   */
  order?: 'ascend' | 'descend' | false | null
}

/** Naive 排序态 → 后端契约值（第三态 false/null = 取消排序，回默认排序） */
function toApiSortOrder(order: PageSorter['order']): SortOrder | undefined {
  if (order === 'ascend') return 'asc'
  if (order === 'descend') return 'desc'
  return undefined
}

export interface UsePageListReturn<T, Q> {
  queryParams: Q
  loading: Ref<boolean>
  data: Ref<T[]>
  total: Ref<number>
  pageNum: Ref<number>
  pageSize: Ref<number>
  sortField: Ref<string | undefined>
  sortOrder: Ref<SortOrder | undefined>
  /** NDataTable 直接可用（配合 remote 属性） */
  pagination: ComputedRef<PaginationProps>
  load: () => Promise<void>
  /** 条件变更后回第一页再查 */
  search: () => Promise<void>
  reset: () => Promise<void>
  setPage: (page: number) => void
  setPageSize: (size: number) => void
  /** 表头排序：把 Naive 的 sorter 转成 sortField/sortOrder 并回第一页 */
  applySorter: (sorter: PageSorter | PageSorter[] | null) => Promise<void>
  error: Ref<string>
}

const DEFAULT_PAGE_SIZES = [10, 20, 50, 100]

/**
 * 列表页样板：查询条件 + 分页 + 排序 + 加载状态。
 * 用法：const list = usePageList({ fetcher: pageUsers, defaultQuery: () => ({ keyword: '' }) })
 *      onMounted(list.search)
 */
export function usePageList<T, Q extends Record<string, unknown> = Record<string, never>>(
  options: PageListOptions<T, Q>
): UsePageListReturn<T, Q> {
  const defaults = options.defaultQuery ?? ((): Q => ({} as Q))
  const queryParams = reactive(defaults()) as Q
  const loading = ref(false)
  const data = ref([]) as Ref<T[]>
  const total = ref(0)
  const pageNum = ref(1)
  const pageSize = ref(options.pageSize ?? 20)
  const sortField = ref<string | undefined>(undefined)
  const sortOrder = ref<SortOrder | undefined>(undefined)
  const error = ref('')

  async function load(): Promise<void> {
    loading.value = true
    error.value = ''
    try {
      const result = await options.fetcher({
        ...(queryParams as Q),
        pageNum: pageNum.value,
        pageSize: pageSize.value,
        sortField: sortField.value,
        sortOrder: sortOrder.value
      })
      data.value = result?.rows ?? []
      total.value = Number(result?.total ?? 0)
      options.onLoaded?.(data.value, total.value)
    } catch (err) {
      // 全局提示已由 http 拦截器负责，这里只保留状态供页面兜底
      data.value = []
      total.value = 0
      error.value = errorText(err, '列表加载失败')
    } finally {
      loading.value = false
    }
  }

  async function search(): Promise<void> {
    pageNum.value = 1
    await load()
  }

  async function reset(): Promise<void> {
    Object.assign(queryParams, defaults())
    sortField.value = undefined
    sortOrder.value = undefined
    pageNum.value = 1
    await load()
  }

  function setPage(page: number): void {
    pageNum.value = page
    void load()
  }

  function setPageSize(size: number): void {
    pageSize.value = size
    pageNum.value = 1
    void load()
  }

  function applySorter(sorter: PageSorter | PageSorter[] | null): Promise<void> {
    const single = Array.isArray(sorter) ? sorter[0] : sorter
    const mapped = single?.columnKey ? toApiSortOrder(single.order) : undefined
    if (!single?.columnKey || !mapped) {
      sortField.value = undefined
      sortOrder.value = undefined
    } else {
      sortField.value = String(single.columnKey)
      sortOrder.value = mapped
    }
    pageNum.value = 1
    return load()
  }

  const pagination = computed<PaginationProps>(() => ({
    page: pageNum.value,
    pageSize: pageSize.value,
    itemCount: total.value,
    showSizePicker: true,
    pageSizes: DEFAULT_PAGE_SIZES,
    prefix: () => `共 ${total.value} 条`,
    onChange: setPage,
    onUpdatePageSize: setPageSize
  }))

  if (options.immediate !== false) void load()

  return {
    queryParams,
    loading,
    data,
    total,
    pageNum,
    pageSize,
    sortField,
    sortOrder,
    pagination,
    load,
    search,
    reset,
    setPage,
    setPageSize,
    applySorter,
    error
  }
}
