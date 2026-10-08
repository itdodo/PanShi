import { describe, expect, it } from 'vitest'
import { usePageList, type PageListOptions } from '@/composables/usePageList'
import type { PagedResult } from '@/api/types'

interface Row {
  id: number
}

function makeList(over: Partial<PageListOptions<Row, { keyword: string }>> = {}) {
  const calls: Array<Record<string, unknown>> = []
  const list = usePageList<Row, { keyword: string }>({
    fetcher: async (query) => {
      calls.push({ ...query })
      return over.fetcher ? await over.fetcher(query) : { total: 3, rows: [{ id: 1 }] }
    },
    defaultQuery: () => ({ keyword: '' }),
    immediate: false,
    ...over
  } as PageListOptions<Row, { keyword: string }>)
  return { list, calls }
}

/**
 * 31 个列表页共用这套分页/排序样板，它的历史事故是：
 * NDataTable 给的排序值是 'ascend'/'descend'/false，直接透传给后端会让 `desc = sortOrder != "asc"` 恒真，
 * 所有「升序」静默变成降序。这里把三态映射钉死。
 */
describe('usePageList 排序三态映射', () => {
  it('ascend → asc，descend → desc', async () => {
    const { list, calls } = makeList()
    await list.applySorter({ columnKey: 'createTime', order: 'ascend' })
    expect(list.sortField.value).toBe('createTime')
    expect(list.sortOrder.value).toBe('asc')
    expect(calls.at(-1)).toMatchObject({ sortField: 'createTime', sortOrder: 'asc' })

    await list.applySorter({ columnKey: 'createTime', order: 'descend' })
    expect(list.sortOrder.value).toBe('desc')
  })

  it('第三态（false / null / 无 columnKey）清空排序并回默认排序', async () => {
    const { list, calls } = makeList()
    await list.applySorter({ columnKey: 'createTime', order: 'ascend' })
    await list.applySorter({ columnKey: 'createTime', order: false })
    expect(list.sortField.value).toBeUndefined()
    expect(list.sortOrder.value).toBeUndefined()
    expect(calls.at(-1)).not.toHaveProperty('sortField', 'createTime')

    await list.applySorter(null)
    expect(list.sortOrder.value).toBeUndefined()
  })

  it('多列排序取第一个，且排序后回第一页', async () => {
    const { list } = makeList()
    await list.search()
    list.setPage(3)
    expect(list.pageNum.value).toBe(3)
    await list.applySorter([{ columnKey: 'a', order: 'ascend' }, { columnKey: 'b', order: 'descend' }])
    expect(list.sortField.value).toBe('a')
    expect(list.pageNum.value).toBe(1)
  })
})

describe('usePageList 分页与查询', () => {
  it('search 回第一页；改每页条数也回第一页', async () => {
    const { list, calls } = makeList()
    await list.load()
    list.setPage(2)
    await list.search()
    expect(calls.at(-1)).toMatchObject({ pageNum: 1 })
    list.setPage(4)
    list.setPageSize(50)
    expect(list.pageNum.value).toBe(1)
    expect(list.pageSize.value).toBe(50)
  })

  it('reset 恢复查询初值并清排序', async () => {
    const { list, calls } = makeList()
    list.queryParams.keyword = 'abc'
    await list.applySorter({ columnKey: 'createTime', order: 'descend' })
    await list.reset()
    expect(list.queryParams.keyword).toBe('')
    expect(list.sortOrder.value).toBeUndefined()
    expect(calls.at(-1)).toMatchObject({ keyword: '', pageNum: 1 })
  })

  it('请求失败时清空列表、置错误文案，且 loading 一定回落', async () => {
    const { list } = makeList({ fetcher: async () => Promise.reject(new Error('库挂了')) })
    await list.load()
    expect(list.data.value).toEqual([])
    expect(list.total.value).toBe(0)
    expect(list.error.value).toBe('库挂了')
    expect(list.loading.value).toBe(false)
  })

  it('分页器带总条数文案与可选页大小', async () => {
    const { list } = makeList()
    await list.load()
    const p = list.pagination.value
    expect(p.itemCount).toBe(3)
    expect(p.pageSizes).toEqual([10, 20, 50, 100])
    expect(p.prefix?.({ page: 1, pageSize: 10, itemCount: 3, startIndex: 0, endIndex: 3, pageCount: 1 })).toBe('共 3 条')
  })

  it('immediate 缺省时构造即加载一次', async () => {
    const calls: Array<Record<string, unknown>> = []
    usePageList<Row, Record<string, never>>({
      fetcher: async (q) => {
        calls.push({ ...q })
        return { total: 0, rows: [] } as PagedResult<Row>
      }
    })
    await Promise.resolve()
    expect(calls.length).toBe(1)
  })
})
