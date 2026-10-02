<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
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
import { ALERT_LEVELS, alertLevelMeta, pageStockAlerts, type StockAlertDto } from '@/api/scm'
import { usePageList } from '@/composables/usePageList'
import { formatQty } from '@/utils/format'

const LEVEL_OPTIONS: SelectOption[] = [
  { label: '缺货', value: ALERT_LEVELS.Short },
  { label: '超储', value: ALERT_LEVELS.Over }
]

const warehouses = ref<SelectOption[]>([])

type Row = StockAlertDto
type QueryModel = { keyword: string; warehouseId: string | null; level: number | null }

const list = usePageList<Row, QueryModel>({
  fetcher: (q) =>
    pageStockAlerts({
      pageNum: q.pageNum,
      pageSize: q.pageSize,
      keyword: q.keyword || undefined,
      warehouseId: q.warehouseId ?? undefined,
      level: q.level ?? undefined
    }),
  defaultQuery: () => ({ keyword: '', warehouseId: null, level: null }),
  pageSize: 20
})

const gap = (row: Row) =>
  h(
    'b',
    { style: `font-variant-numeric: tabular-nums; color: ${row.level === ALERT_LEVELS.Short ? '#d03050' : '#a07800'}` },
    row.level === ALERT_LEVELS.Short ? `还差 ${formatQty(row.gap)}` : `超出 ${formatQty(row.gap)}`
  )

const columns = computed<DataTableColumns<Row>>(() => [
  { title: '仓库', key: 'warehouseName', minWidth: 140, ellipsis: { tooltip: true } },
  { title: '物料编码', key: 'materialCode', width: 140 },
  { title: '物料名称', key: 'materialName', minWidth: 170, ellipsis: { tooltip: true } },
  {
    title: '规格型号',
    key: 'spec',
    minWidth: 160,
    ellipsis: { tooltip: true },
    render: (row) => row.spec ?? h('span', { class: 'ps-muted' }, '-')
  },
  { title: '单位', key: 'unit', width: 76, render: (row) => row.unit ?? h('span', { class: 'ps-muted' }, '-') },
  {
    title: '现存量',
    key: 'quantity',
    width: 118,
    render: (row) => h('span', { style: 'font-variant-numeric: tabular-nums' }, formatQty(row.quantity))
  },
  {
    title: '预警区间',
    key: 'minStock',
    width: 140,
    render: (row) => `${row.minStock ?? '—'} ~ ${row.maxStock ?? '—'}`
  },
  {
    title: '档位',
    key: 'level',
    width: 96,
    render: (row) => {
      const meta = alertLevelMeta(row.level)
      return h(NTag, { size: 'small', bordered: false, type: meta.type }, { default: () => meta.label })
    }
  },
  { title: '缺口 / 超出', key: 'gap', width: 150, fixed: 'right', render: gap }
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
              placeholder="物料编码 / 名称"
              clearable
              style="width: 210px"
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
          <NFormItem label="档位">
            <NSelect v-model:value="list.queryParams.level" :options="LEVEL_OPTIONS" placeholder="全部" clearable style="width: 120px" />
          </NFormItem>
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="list.reset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" class="ps-scm__toolbar" :wrap="true">
        <span class="ps-muted">阈值在「基础资料 → 物料」的预警上下限里维护；没有台账行按 0 存量计；按缺口大小倒序</span>
        <span class="ps-muted">共 {{ list.total.value }} 条</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: Row) => `${row.warehouseId}-${row.materialId}`"
        :scroll-x="1320"
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
