<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import dayjs from 'dayjs'
import {
  NButton,
  NCard,
  NCheckbox,
  NDataTable,
  NDatePicker,
  NForm,
  NFormItem,
  NInput,
  NModal,
  NSelect,
  NSpace,
  NTag,
  type DataTableColumns,
  type FormInst,
  type FormRules,
  type SelectOption
} from 'naive-ui'
import { supplierOptions } from '@/api/basedata'
import {
  ARRIVAL_STATUS,
  arrivalStatusMeta,
  pageArrivals,
  rescheduleArrival,
  type ArrivalDto
} from '@/api/scm'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDate, formatQty } from '@/utils/format'

/**
 * 到货计划（/scm/arrival）：计划由采购订单批准后按行生成，这里只能看与改期。
 * 「已收」列来自已过账且回链了订单号的采购入库单，所以这页不是收货入口——
 * 收货仍然走去出入库单，避免两处录数对不上。
 */
const STATUS_OPTIONS: SelectOption[] = [
  { label: '待到货', value: ARRIVAL_STATUS.Pending },
  { label: '部分到货', value: ARRIVAL_STATUS.Partial },
  { label: '已收满', value: ARRIVAL_STATUS.Done }
]

const suppliers = ref<SelectOption[]>([])
const dueValue = ref<number | null>(null)

type Row = ArrivalDto
type QueryModel = {
  keyword: string
  supplierId: string | null
  status: number | null
  overdueOnly: boolean
  mine: boolean
  dueBefore?: string
}

const list = usePageList<Row, QueryModel>({
  fetcher: (q) =>
    pageArrivals({
      pageNum: q.pageNum,
      pageSize: q.pageSize,
      keyword: q.keyword || undefined,
      supplierId: q.supplierId ?? undefined,
      status: q.status ?? undefined,
      overdueOnly: q.overdueOnly || undefined,
      mine: q.mine,
      dueBefore: q.dueBefore
    }),
  defaultQuery: () => ({ keyword: '', supplierId: null, status: null, overdueOnly: false, mine: false, dueBefore: undefined }),
  pageSize: 20
})

function onDueUpdate(value: number | null): void {
  dueValue.value = value ?? null
  list.queryParams.dueBefore = value ? dayjs(Number(value)).format('YYYY-MM-DD') : undefined
}

async function onReset(): Promise<void> {
  dueValue.value = null
  await list.reset()
}

/* -------------------------------- 改期弹窗 -------------------------------- */
const modalVisible = ref(false)
const saving = ref(false)
const formRef = ref<FormInst | null>(null)
const form = reactive({ id: '', orderNo: '', material: '', planDate: Date.now(), remark: '', version: 0 })

const rules: FormRules = {
  planDate: [{ required: true, type: 'number', message: '请选择新的计划到货日', trigger: ['change', 'blur'] }]
}

function openReschedule(row: Row): void {
  form.id = row.id
  form.orderNo = row.orderNo
  form.material = `${row.materialCode} ${row.materialName}`
  form.planDate = dayjs(row.planDate).valueOf()
  form.remark = row.remark ?? ''
  form.version = row.version
  modalVisible.value = true
}

async function saveReschedule(): Promise<void> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return
  saving.value = true
  try {
    await rescheduleArrival(form.id, {
      planDate: dayjs(form.planDate).format('YYYY-MM-DD'),
      remark: form.remark.trim() || null,
      version: form.version
    })
    message.success('计划到货日已更新')
    modalVisible.value = false
    await list.load()
  } catch {
    /* 拦截器已提示（含并发冲突），弹窗保持打开 */
  } finally {
    saving.value = false
  }
}

/* --------------------------------- 列表列 --------------------------------- */
const columns = computed<DataTableColumns<Row>>(() => [
  { title: '订单号', key: 'orderNo', width: 140 },
  { title: '供应商', key: 'supplierName', minWidth: 150, ellipsis: { tooltip: true } },
  { title: '物料编码', key: 'materialCode', width: 140 },
  { title: '物料名称', key: 'materialName', minWidth: 160, ellipsis: { tooltip: true } },
  {
    title: '计划 / 已收 / 未收',
    key: 'planQty',
    width: 190,
    render: (row) =>
      h('span', { style: 'font-variant-numeric: tabular-nums' }, [
        `${formatQty(row.planQty)} / ${formatQty(row.receivedQty)} / `,
        row.openQty > 0
          ? h('b', { style: 'color:#d03050' }, formatQty(row.openQty))
          : h('span', { class: 'ps-muted' }, formatQty(row.openQty))
      ])
  },
  {
    title: '计划到货日',
    key: 'planDate',
    width: 140,
    render: (row) =>
      row.overdue
        ? h('span', { style: 'color:#d03050; font-weight:600' }, `${formatDate(row.planDate)} 逾期`)
        : formatDate(row.planDate)
  },
  {
    title: '进度',
    key: 'status',
    width: 110,
    render: (row) => {
      const meta = arrivalStatusMeta(row.status)
      return h(NTag, { size: 'small', bordered: false, type: meta.type }, { default: () => meta.label })
    }
  },
  {
    title: '改过期',
    key: 'rescheduled',
    width: 90,
    render: (row) => (row.rescheduled ? h('span', { style: 'color:#a07800' }, '是') : h('span', { class: 'ps-muted' }, '—'))
  },
  { title: '跟单员', key: 'ownerUserName', width: 100 },
  {
    title: '备注',
    key: 'remark',
    minWidth: 150,
    ellipsis: { tooltip: true },
    render: (row) => row.remark ?? h('span', { class: 'ps-muted' }, '-')
  },
  {
    title: '操作',
    key: 'actions',
    width: 90,
    fixed: 'right',
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm('scm:arrival:edit')
            ? h(NButton, { key: 'reschedule', size: 'tiny', text: true, type: 'primary', onClick: () => openReschedule(row) }, { default: () => '改期' })
            : null
        ].filter(Boolean)
      })
  }
])

onMounted(async () => {
  try {
    suppliers.value = await supplierOptions()
  } catch {
    suppliers.value = []
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
              placeholder="订单号 / 供应商 / 物料"
              clearable
              style="width: 220px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="供应商">
            <NSelect
              v-model:value="list.queryParams.supplierId"
              :options="suppliers"
              placeholder="全部"
              clearable
              filterable
              style="width: 190px"
            />
          </NFormItem>
          <NFormItem label="进度">
            <NSelect v-model:value="list.queryParams.status" :options="STATUS_OPTIONS" placeholder="全部" clearable style="width: 120px" />
          </NFormItem>
          <NFormItem label="计划日截止">
            <NDatePicker :value="dueValue" type="date" clearable style="width: 150px" @update:value="onDueUpdate" />
          </NFormItem>
          <NCheckbox v-model:checked="list.queryParams.overdueOnly" @update:checked="list.search">只看逾期</NCheckbox>
          <NCheckbox v-model:checked="list.queryParams.mine" @update:checked="list.search">只看我的</NCheckbox>
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="onReset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" class="ps-scm__toolbar" :wrap="true">
        <span class="ps-muted">
          计划由采购订单批准后按行生成；「已收」取自已过账且回链了订单号的采购入库单，收货仍然走去出入库单
        </span>
        <span class="ps-muted">共 {{ list.total.value }} 条</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: Row) => row.id"
        :scroll-x="1500"
        size="small"
        :bordered="false"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="card"
      title="改期"
      style="width: 460px; max-width: 96vw"
      :bordered="false"
      :mask-closable="false"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="top">
        <NFormItem label="订单号">
          <span class="ps-muted">{{ form.orderNo }} · {{ form.material }}</span>
        </NFormItem>
        <NFormItem label="新的计划到货日" path="planDate">
          <NDatePicker v-model:value="form.planDate" type="date" style="width: 100%" />
        </NFormItem>
        <NFormItem label="改期原因" path="remark">
          <NInput
            v-model:value="form.remark"
            type="textarea"
            :rows="2"
            maxlength="256"
            show-count
            placeholder="供应商产能、客户改需求等"
          />
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="modalVisible = false">取消</NButton>
          <NButton type="primary" :loading="saving" @click="saveReschedule">保存</NButton>
        </NSpace>
      </template>
    </NModal>
  </div>
</template>

<style scoped>
.ps-scm__toolbar {
  margin: 16px 0 12px;
}
</style>
