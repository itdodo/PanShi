<script setup lang="ts">
import { computed, h } from 'vue'
import { NButton, NDataTable, NInput, NInputNumber, NSelect, NSpace, type DataTableColumns } from 'naive-ui'
import { lineAmount, type OrderLineForm } from '@/api/scm'

/**
 * 订单明细行编辑器（采购/销售共用，两者行形状完全同构）。
 * 金额只做展示：提交后以后端回传的 amount/totalAmount 为准，
 * 这里的 lineAmount 与后端是同一套 half-up 到分口径，改一处必须改另一处。
 */
/** 物料下拉项：value=id，label=编码+名称(+规格)，spec/unit 用于选中后带出只读列 */
export interface MaterialChoice {
  value: string
  label: string
  spec?: string | null
  unit?: string | null
}

const props = defineProps<{
  materials: MaterialChoice[]
  readonly?: boolean
  /** 库存单据只要数量：隐藏单价/税率/金额三列，合计也只报数量 */
  quantityOnly?: boolean
  /** 选料后带出协议价（采购页传入；不传就不自动填价） */
  quote?: (materialId: string) => Promise<{ unitPrice: number; taxRate: number } | null>
}>()

const lines = defineModel<OrderLineForm[]>({ required: true })

const materialOptions = computed(() => props.materials.map((m) => ({ label: m.label, value: m.value })))

const byId = computed(() => new Map(props.materials.map((m) => [m.value, m])))

const totals = computed(() => ({
  qty: lines.value.reduce((sum, l) => sum + (Number(l.quantity) || 0), 0),
  amount: Math.round(lines.value.reduce((sum, l) => sum + lineAmount(l.quantity, l.unitPrice), 0) * 100) / 100
}))

async function onMaterial(row: OrderLineForm): Promise<void> {
  if (!props.quote) return
  const hit = await props.quote(row.materialId)
  if (!hit) return
  row.unitPrice = hit.unitPrice
  row.taxRate = hit.taxRate
}

function addLine(): void {
  lines.value = [...lines.value, { materialId: '', quantity: 1, unitPrice: 0, taxRate: 13, remark: null }]
}

function removeLine(index: number): void {
  lines.value = lines.value.filter((_, i) => i !== index)
}

const columns = computed<DataTableColumns<OrderLineForm>>(() => [
  {
    title: '#',
    key: 'index',
    width: 44,
    render: (_, index) => h('span', { class: 'ps-muted' }, String(index + 1))
  },
  {
    title: '物料',
    key: 'materialId',
    minWidth: 230,
    render: (row) =>
      h(NSelect, {
        value: row.materialId || null,
        options: materialOptions.value,
        filterable: true,
        tag: false,
        disabled: props.readonly,
        placeholder: '选择物料',
        size: 'small',
        'onUpdate:value': (v: string | null) => {
          row.materialId = v ?? ''
          void onMaterial(row)
        }
      })
  },
  {
    title: '规格',
    key: 'spec',
    width: 120,
    render: (row) => h('span', { class: 'ps-muted' }, byId.value.get(row.materialId)?.spec ?? '-')
  },
  {
    title: '单位',
    key: 'unit',
    width: 68,
    render: (row) => h('span', { class: 'ps-muted' }, byId.value.get(row.materialId)?.unit ?? '-')
  },
  {
    title: '数量',
    key: 'quantity',
    width: 118,
    render: (row) =>
      h(NInputNumber, {
        value: row.quantity,
        min: 0,
        precision: 4,
        size: 'small',
        disabled: props.readonly,
        'onUpdate:value': (v: number | null) => (row.quantity = v ?? 0)
      })
  },
  ...(props.quantityOnly
    ? []
    : [
        {
          title: '含税单价',
          key: 'unitPrice',
          width: 128,
          render: (row) =>
            h(NInputNumber, {
              value: row.unitPrice,
              min: 0,
              precision: 4,
              size: 'small',
              disabled: props.readonly,
              'onUpdate:value': (v: number | null) => (row.unitPrice = v ?? 0)
            })
        } as DataTableColumns<OrderLineForm>[number],
        {
          title: '税率%',
          key: 'taxRate',
          width: 104,
          render: (row) =>
            h(NInputNumber, {
              value: row.taxRate,
              min: 0,
              max: 100,
              precision: 2,
              size: 'small',
              disabled: props.readonly,
              'onUpdate:value': (v: number | null) => (row.taxRate = v ?? 0)
            })
        } as DataTableColumns<OrderLineForm>[number],
        {
          title: '金额',
          key: 'amount',
          width: 110,
          render: (row) =>
            h('span', { style: 'font-variant-numeric: tabular-nums' },
              lineAmount(row.quantity, row.unitPrice).toFixed(2))
        } as DataTableColumns<OrderLineForm>[number]
      ]),
  {
    title: '备注',
    key: 'remark',
    minWidth: 130,
    render: (row) =>
      h(NInput, {
        value: row.remark ?? '',
        size: 'small',
        maxlength: 256,
        disabled: props.readonly,
        'onUpdate:value': (v: string) => (row.remark = v || null)
      })
  },
  ...(props.readonly
    ? []
    : [
        {
          title: '',
          key: 'ops',
          width: 56,
          render: (_: OrderLineForm, index: number) =>
            h(NButton, {
              size: 'tiny',
              text: true,
              type: 'error',
              onClick: () => removeLine(index)
            }, { default: () => '删行' })
        } as DataTableColumns<OrderLineForm>[number]
      ])
])
</script>

<template>
  <div class="ps-order-lines">
    <NSpace align="center" justify="space-between" :wrap="true" class="ps-order-lines__bar">
      <NButton v-if="!props.readonly" size="small" tertiary type="primary" @click="addLine">+ 添加明细行</NButton>
      <span v-else class="ps-muted">只读</span>
      <span class="ps-muted">
        合计 <b>{{ totals.qty }}</b> 数量<template v-if="!props.quantityOnly"> /
        <b>{{ totals.amount.toFixed(2) }}</b> 金额（后端复核）</template>
      </span>
    </NSpace>
    <NDataTable
      :columns="columns"
      :data="lines"
      :row-key="(row: OrderLineForm) => lines.indexOf(row)"
      size="small"
      :bordered="false"
      :max-height="300"
      :scroll-x="props.quantityOnly ? 820 : 1150"
    />
  </div>
</template>

<style scoped>
.ps-order-lines__bar {
  margin-bottom: 8px;
}
</style>
