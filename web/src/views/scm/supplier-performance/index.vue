<script setup lang="ts">
import { computed, h, ref } from 'vue'
import dayjs from 'dayjs'
import {
  NButton,
  NCard,
  NDataTable,
  NDatePicker,
  NForm,
  NFormItem,
  NInput,
  NSpace,
  NTag,
  type DataTableColumns
} from 'naive-ui'
import { pageSupplierPerformance, type SupplierPerformanceDto } from '@/api/scm'
import { usePageList } from '@/composables/usePageList'
import { formatQty } from '@/utils/format'

/**
 * 供应商绩效（/scm/supplier-performance）：只读报表，按下单日期区间统计。
 * 订单与入库的关联靠采购入库单回填「来源单号」，没回填的会被算进「未到货」——
 * 所以这一列非零不一定是供应商没交货，先把入库单的回链补全再下结论。
 */
function defaultBegin(): number {
  return dayjs().subtract(1, 'year').valueOf()
}

const rangeValue = ref<[number, number] | null>([defaultBegin(), Date.now()])

type Row = SupplierPerformanceDto
type QueryModel = { keyword: string; begin?: string; end?: string }

const list = usePageList<Row, QueryModel>({
  fetcher: (q) =>
    pageSupplierPerformance({
      pageNum: q.pageNum,
      pageSize: q.pageSize,
      keyword: q.keyword || undefined,
      begin: q.begin,
      end: q.end
    }),
  defaultQuery: () => ({
    keyword: '',
    begin: dayjs(defaultBegin()).format('YYYY-MM-DD'),
    end: dayjs().format('YYYY-MM-DD')
  }),
  pageSize: 20
})

function onRangeUpdate(value: number | [number, number] | null): void {
  const pair = Array.isArray(value) ? value : null
  rangeValue.value = pair ? [Number(pair[0]), Number(pair[1])] : null
  list.queryParams.begin = pair ? dayjs(Number(pair[0])).format('YYYY-MM-DD') : dayjs(defaultBegin()).format('YYYY-MM-DD')
  list.queryParams.end = pair ? dayjs(Number(pair[1])).format('YYYY-MM-DD') : dayjs().format('YYYY-MM-DD')
}

async function onReset(): Promise<void> {
  rangeValue.value = [defaultBegin(), Date.now()]
  await list.reset()
}

const money = (value: number) => h('span', { style: 'font-variant-numeric: tabular-nums' }, `¥${value.toFixed(2)}`)

function rateTag(row: Row) {
  if (row.onTimeRate == null) return h('span', { class: 'ps-muted' }, '—')
  const pct = Math.round(row.onTimeRate * 1000) / 10
  const type = pct >= 90 ? 'success' : pct >= 70 ? 'warning' : 'error'
  return h(NTag, { size: 'small', bordered: false, type }, { default: () => `${pct}%（${row.onTime}/${row.onTimeBase}）` })
}

const columns = computed<DataTableColumns<Row>>(() => [
  { title: '供应商', key: 'supplierName', minWidth: 180, ellipsis: { tooltip: true } },
  { title: '订单数', key: 'orders', width: 96, render: (row) => String(row.orders) },
  { title: '订单金额', key: 'amount', width: 140, render: (row) => money(row.amount) },
  { title: '已到货', key: 'delivered', width: 96, render: (row) => String(row.delivered) },
  {
    title: '未到货',
    key: 'pending',
    width: 96,
    render: (row) => (row.pending ? h('span', { style: 'color:#d03050' }, String(row.pending)) : h('span', { class: 'ps-muted' }, '0'))
  },
  { title: '准交率', key: 'onTimeRate', width: 150, render: rateTag },
  {
    title: '平均交付天数',
    key: 'avgLeadDays',
    width: 130,
    render: (row) => (row.delivered ? `${formatQty(row.avgLeadDays, 1)} 天` : h('span', { class: 'ps-muted' }, '—'))
  }
])
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NForm inline :model="list.queryParams" label-placement="left" :show-feedback="false">
        <NSpace :size="12" align="center" wrap>
          <NFormItem label="关键字">
            <NInput
              v-model:value="list.queryParams.keyword"
              placeholder="供应商名称"
              clearable
              style="width: 200px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="下单日期">
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
          只统计已批准的采购订单；准交率分母＝已到货且填了计划交期的订单，括号内是「按期 / 分母」
        </span>
        <span class="ps-muted">共 {{ list.total.value }} 家</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: Row) => row.supplierId"
        :scroll-x="1000"
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
