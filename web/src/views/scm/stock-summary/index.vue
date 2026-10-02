<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import {
  NButton,
  NCard,
  NDataTable,
  NDatePicker,
  NForm,
  NFormItem,
  NInput,
  NSelect,
  NSpace,
  type DataTableColumns,
  type SelectOption
} from 'naive-ui'
import { warehouseOptions } from '@/api/basedata'
import { pageStockSummary, type StockSummaryDto } from '@/api/scm'
import { usePageList } from '@/composables/usePageList'
import { formatQty } from '@/utils/format'

/**
 * 进销存汇总（/scm/stock-summary）：只读报表，数据全部来自库存流水。
 * 起始日必填（期初要按它回看历史），默认本月 1 号到今天；清空区间会自动退回默认值而不是报错。
 */
function monthStart(): string {
  return dayjs().startOf('month').format('YYYY-MM-DD')
}

function today(): string {
  return dayjs().format('YYYY-MM-DD')
}

function rangeToPair(value: number | [number, number] | null): [string, string] {
  const pair = Array.isArray(value) ? value : null
  return pair
    ? [dayjs(Number(pair[0])).format('YYYY-MM-DD'), dayjs(Number(pair[1])).format('YYYY-MM-DD')]
    : [monthStart(), today()]
}

const rangeValue = ref<[number, number] | null>([dayjs().startOf('month').valueOf(), dayjs().valueOf()])

const warehouses = ref<SelectOption[]>([])

type Row = StockSummaryDto
type QueryModel = { keyword: string; warehouseId: string | null; begin: string; end: string }

const list = usePageList<Row, QueryModel>({
  fetcher: (q) =>
    pageStockSummary({
      pageNum: q.pageNum,
      pageSize: q.pageSize,
      keyword: q.keyword || undefined,
      warehouseId: q.warehouseId ?? undefined,
      begin: q.begin,
      end: q.end
    }),
  defaultQuery: () => ({ keyword: '', warehouseId: null, begin: monthStart(), end: today() }),
  pageSize: 20
})

function onRangeUpdate(value: number | [number, number] | null): void {
  const pair = Array.isArray(value) ? value : null
  rangeValue.value = pair ? [Number(pair[0]), Number(pair[1])] : null
  ;[list.queryParams.begin, list.queryParams.end] = rangeToPair(value)
}

async function onReset(): Promise<void> {
  rangeValue.value = [dayjs().startOf('month').valueOf(), dayjs().valueOf()]
  await list.reset()
}

const num = (value: number, tone?: 'in' | 'out') =>
  h(
    'span',
    {
      style: `font-variant-numeric: tabular-nums;${
        tone === 'in' && value ? ' color:#18a058' : tone === 'out' && value ? ' color:#d03050' : ''
      }`
    },
    formatQty(value)
  )

const columns = computed<DataTableColumns<Row>>(() => [
  { title: '物料编码', key: 'materialCode', width: 140 },
  { title: '物料名称', key: 'materialName', minWidth: 170, ellipsis: { tooltip: true } },
  {
    title: '规格型号',
    key: 'spec',
    minWidth: 150,
    ellipsis: { tooltip: true },
    render: (row) => row.spec ?? h('span', { class: 'ps-muted' }, '-')
  },
  { title: '仓库', key: 'warehouseName', minWidth: 130, ellipsis: { tooltip: true } },
  { title: '单位', key: 'unit', width: 72, render: (row) => row.unit ?? h('span', { class: 'ps-muted' }, '-') },
  { title: '期初结存', key: 'opening', width: 112, render: (row) => num(row.opening) },
  { title: '本期收入', key: 'inbound', width: 112, render: (row) => num(row.inbound, 'in') },
  { title: '本期发出', key: 'outbound', width: 112, render: (row) => num(row.outbound, 'out') },
  { title: '期末结存', key: 'closing', width: 116, render: (row) => h('b', { style: 'font-variant-numeric: tabular-nums' }, formatQty(row.closing)) },
  { title: '笔数', key: 'entries', width: 80, render: (row) => (row.entries ? String(row.entries) : h('span', { class: 'ps-muted' }, '—')) }
])

const totals = computed(() => {
  const sum = (pick: (r: Row) => number) => list.data.value.reduce((a, r) => a + pick(r), 0)
  return {
    opening: sum((r) => r.opening),
    inbound: sum((r) => r.inbound),
    outbound: sum((r) => r.outbound),
    closing: sum((r) => r.closing)
  }
})

onMounted(async () => {
  try {
    warehouses.value = await warehouseOptions()
  } catch {
    warehouses.value = []
  }
})
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NForm inline :model="list.queryParams" label-placement="left" :show-feedback="false">
        <NSpace :size="12" align="center" wrap>
          <NFormItem label="关键字">
            <NInput
              v-model:value="list.queryParams.keyword"
              placeholder="物料编码 / 名称"
              clearable
              style="width: 200px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="仓库">
            <NSelect
              v-model:value="list.queryParams.warehouseId"
              :options="warehouses"
              placeholder="全部"
              clearable
              style="width: 190px"
            />
          </NFormItem>
          <NFormItem label="统计区间">
            <NDatePicker :value="rangeValue" type="daterange" clearable style="width: 250px" @update:value="onRangeUpdate" />
          </NFormItem>
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="onReset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" class="ps-scm__toolbar" :wrap="true">
        <span class="ps-muted">
          本页合计：期初 {{ formatQty(totals.opening) }} · 收入 {{ formatQty(totals.inbound) }} · 发出
          {{ formatQty(totals.outbound) }} · 期末 {{ formatQty(totals.closing) }}
        </span>
        <span class="ps-muted">共 {{ list.total.value }} 行 · 期初 + 收入 − 发出 = 期末</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: Row) => `${row.warehouseId}-${row.materialId}`"
        :scroll-x="1250"
        size="small"
        :bordered="false"
      />
    </NCard>
  </div>
</template>

<style scoped>
.ps-scm__toolbar {
  margin: 16px 0 12px;
}
</style>
