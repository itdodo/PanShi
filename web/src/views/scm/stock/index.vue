<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import {
  NButton,
  NCard,
  NCheckbox,
  NDataTable,
  NForm,
  NFormItem,
  NInput,
  NSelect,
  NSpace,
  type DataTableColumns,
  type SelectOption
} from 'naive-ui'
import { warehouseOptions } from '@/api/basedata'
import { pageStocks, type StockDto } from '@/api/scm'
import { usePageList } from '@/composables/usePageList'
import { formatDateTime, formatQty } from '@/utils/format'

/**
 * 库存台账（/scm/stock）：仓库 × 物料一行现存量。
 * 只读——任何变动都必须走出入库单过账，这样台账永远等于流水累计和。
 */
const warehouses = ref<SelectOption[]>([])

type Row = StockDto
type QueryModel = { keyword: string; warehouseId: string | null; onlyPositive: boolean }

const list = usePageList<Row, QueryModel>({
  fetcher: (q) =>
    pageStocks({
      pageNum: q.pageNum,
      pageSize: q.pageSize,
      sortField: q.sortField,
      sortOrder: q.sortOrder,
      keyword: q.keyword || undefined,
      warehouseId: q.warehouseId ?? undefined,
      onlyPositive: q.onlyPositive
    }),
  defaultQuery: () => ({ keyword: '', warehouseId: null, onlyPositive: false }),
  pageSize: 20
})

const columns = computed<DataTableColumns<Row>>(() => [
  { title: '仓库', key: 'warehouseName', minWidth: 140, ellipsis: { tooltip: true } },
  { title: '物料编码', key: 'materialCode', width: 150 },
  { title: '物料名称', key: 'materialName', minWidth: 170, ellipsis: { tooltip: true } },
  {
    title: '规格型号',
    key: 'spec',
    minWidth: 160,
    ellipsis: { tooltip: true },
    render: (row) => row.spec ?? h('span', { class: 'ps-muted' }, '-')
  },
  { title: '单位', key: 'unit', width: 80, render: (row) => row.unit ?? h('span', { class: 'ps-muted' }, '-') },
  {
    title: '现存量',
    key: 'quantity',
    width: 130,
    render: (row) =>
      h(
        'b',
        { style: `font-variant-numeric: tabular-nums; color: ${row.quantity <= 0 ? 'var(--ps-text-3)' : '#18a058'}` },
        formatQty(row.quantity)
      )
  },
  {
    title: '最后变动',
    key: 'updateTime',
    width: 175,
    render: (row) => (row.updateTime ? formatDateTime(row.updateTime) : h('span', { class: 'ps-muted' }, '-'))
  }
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
              placeholder="物料编码 / 名称 / 仓库"
              clearable
              style="width: 220px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="仓库">
            <NSelect
              v-model:value="list.queryParams.warehouseId"
              :options="warehouses"
              placeholder="全部"
              clearable
              style="width: 180px"
            />
          </NFormItem>
          <NFormItem>
            <NCheckbox v-model:checked="list.queryParams.onlyPositive" @update:checked="list.search">
              只看有货
            </NCheckbox>
          </NFormItem>
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="list.reset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" class="ps-scm__toolbar" :wrap="true">
        <span class="ps-muted">台账只读：库存变动一律经「出入库单 → 过账」</span>
        <span class="ps-muted">共 {{ list.total.value }} 条</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: Row) => row.id"
        :scroll-x="1150"
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
