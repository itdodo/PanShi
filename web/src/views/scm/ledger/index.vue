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
  NTag,
  type DataTableColumns,
  type SelectOption
} from 'naive-ui'
import { warehouseOptions } from '@/api/basedata'
import { pageLedger, STOCK_KINDS, stockKindLabel, type LedgerDto } from '@/api/scm'
import { usePageList } from '@/composables/usePageList'
import { formatDateTime, formatQty } from '@/utils/format'

/**
 * 库存流水（/scm/ledger）：只读账本。每过账一行记一条，带变动前/变动后结存，
 * 对账与追溯不必重放历史；调拨一单会留下出、入两条。
 */
const KIND_OPTIONS: SelectOption[] = STOCK_KINDS.map((k) => ({ label: k.label, value: k.value }))
const warehouses = ref<SelectOption[]>([])

type Row = LedgerDto
type QueryModel = {
  keyword: string
  kind: number | null
  warehouseId: string | null
  begin?: string
  end?: string
}

const list = usePageList<Row, QueryModel>({
  fetcher: (q) =>
    pageLedger({
      pageNum: q.pageNum,
      pageSize: q.pageSize,
      sortField: q.sortField,
      sortOrder: q.sortOrder,
      keyword: q.keyword || undefined,
      kind: q.kind ?? undefined,
      warehouseId: q.warehouseId ?? undefined,
      begin: q.begin,
      end: q.end
    }),
  defaultQuery: () => ({ keyword: '', kind: null, warehouseId: null, begin: undefined, end: undefined }),
  pageSize: 20
})

const rangeValue = ref<[number, number] | null>(null)

function onRangeUpdate(value: number | [number, number] | null): void {
  const pair = Array.isArray(value) ? value : null
  rangeValue.value = pair ? [Number(pair[0]), Number(pair[1])] : null
  list.queryParams.begin = pair ? dayjs(pair[0]).startOf('day').format('YYYY-MM-DDTHH:mm:ss') : undefined
  list.queryParams.end = pair ? dayjs(pair[1]).endOf('day').format('YYYY-MM-DDTHH:mm:ss') : undefined
}

async function onReset(): Promise<void> {
  rangeValue.value = null
  await list.reset()
}

const qty = (value: number, colored = false) =>
  h(
    'span',
    {
      style: `font-variant-numeric: tabular-nums;${
        colored ? (value > 0 ? ' color:#18a058; font-weight:600' : value < 0 ? ' color:#d03050; font-weight:600' : '') : ''
      }`
    },
    (value > 0 && colored ? '+' : '') + formatQty(value)
  )

const columns = computed<DataTableColumns<Row>>(() => [
  { title: '变动时间', key: 'bizTime', width: 175, sorter: true, render: (row) => formatDateTime(row.bizTime) },
  { title: '单号', key: 'docNo', width: 150 },
  {
    title: '类型',
    key: 'kind',
    width: 104,
    render: (row) => h(NTag, { size: 'small', bordered: false }, { default: () => stockKindLabel(row.kind) })
  },
  { title: '仓库', key: 'warehouseName', minWidth: 130, ellipsis: { tooltip: true } },
  { title: '物料编码', key: 'materialCode', width: 140 },
  { title: '物料名称', key: 'materialName', minWidth: 160, ellipsis: { tooltip: true } },
  {
    title: '单位',
    key: 'unit',
    width: 76,
    render: (row) => row.unit ?? h('span', { class: 'ps-muted' }, '-')
  },
  { title: '变动量', key: 'changeQty', width: 116, render: (row) => qty(row.changeQty, true) },
  { title: '变动前', key: 'beforeQty', width: 110, render: (row) => qty(row.beforeQty) },
  { title: '变动后', key: 'afterQty', width: 110, render: (row) => qty(row.afterQty) },
  { title: '操作人', key: 'operatorName', width: 110 }
])

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
              placeholder="单号 / 物料 / 仓库"
              clearable
              style="width: 210px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="类型">
            <NSelect v-model:value="list.queryParams.kind" :options="KIND_OPTIONS" placeholder="全部" clearable style="width: 130px" />
          </NFormItem>
          <NFormItem label="仓库">
            <NSelect v-model:value="list.queryParams.warehouseId" :options="warehouses" placeholder="全部" clearable style="width: 170px" />
          </NFormItem>
          <NFormItem label="变动时间">
            <NDatePicker :value="rangeValue" type="daterange" clearable style="width: 250px" @update:value="onRangeUpdate" />
          </NFormItem>
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="onReset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" class="ps-scm__toolbar" :wrap="true">
        <span class="ps-muted">流水只读，由过账与作废自动写入</span>
        <span class="ps-muted">共 {{ list.total.value }} 条</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: Row) => row.id"
        :scroll-x="1420"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>
  </div>
</template>

<style scoped>
.ps-scm__toolbar {
  margin: 16px 0 12px;
}
</style>
