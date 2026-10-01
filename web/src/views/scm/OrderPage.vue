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
  NGrid,
  NGridItem,
  NInput,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NTag,
  type DataTableColumns,
  type FormInst,
  type FormRules,
  type SelectOption
} from 'naive-ui'
import { DOC_STATUS } from '@/api/biz'
import { pageMaterials } from '@/api/basedata'
import type { MaterialChoice, OrderLineForm } from '@/api/scm'
import { docStatusMeta } from '@/components/flowEnums'
import OrderLinesEditor from '@/components/OrderLinesEditor.vue'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDate, formatDateTime } from '@/utils/format'
import type { Option, OrderKind, OrderRow } from './orderKind'

/**
 * 供应链订单页本体（采购/销售共用，差别由 kind 描述符注入）。
 * 草稿/拒绝/撤回可改可删可提交；审批中与已通过锁定——与报销、采购申请同一套状态语义。
 * 明细行的金额只做展示，提交后以后端回传的 amount/totalAmount 为准。
 */
const props = defineProps<{ kind: OrderKind }>()
const kind = props.kind

/* ------------------------------- 下拉数据源 ------------------------------- */
const partnerOpts = ref<Option[]>([])
const extraOpts = ref<Option[]>([])
const materialOpts = ref<MaterialChoice[]>([])

async function loadOptions(): Promise<void> {
  try {
    partnerOpts.value = await kind.partnerOptions()
  } catch {
    partnerOpts.value = []
  }
  try {
    extraOpts.value = await kind.extraOptions()
  } catch {
    extraOpts.value = []
  }
  try {
    // 明细行要按 id 反查规格/单位，所以取一页启用物料当本地字典（主数据量小，P1 若涨再换分页搜索）
    const page = await pageMaterials({ pageNum: 1, pageSize: 200, status: 0 })
    materialOpts.value = page.rows.map((m) => ({
      value: m.id,
      label: m.spec ? `${m.materialCode} ${m.materialName} / ${m.spec}` : `${m.materialCode} ${m.materialName}`,
      spec: m.spec,
      unit: m.unit
    }))
  } catch {
    materialOpts.value = []
  }
}

/* ---------------------------------- 列表 ---------------------------------- */
type QueryModel = {
  keyword: string
  status: number | null
  partnerId: string | null
  mine: boolean
  begin?: string
  end?: string
}

const list = usePageList<OrderRow, QueryModel>({
  fetcher: (q) =>
    kind.page({
      pageNum: q.pageNum,
      pageSize: q.pageSize,
      sortField: q.sortField,
      sortOrder: q.sortOrder,
      keyword: q.keyword || undefined,
      status: q.status,
      mine: q.mine,
      begin: q.begin,
      end: q.end,
      [kind.partnerFieldName]: q.partnerId ?? undefined
    }),
  defaultQuery: () => ({ keyword: '', status: null, partnerId: null, mine: false, begin: undefined, end: undefined }),
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

const STATUS_OPTIONS: SelectOption[] = [
  { label: '草稿', value: DOC_STATUS.Draft },
  { label: '审批中', value: DOC_STATUS.Running },
  { label: '已通过', value: DOC_STATUS.Approved },
  { label: '已拒绝', value: DOC_STATUS.Rejected },
  { label: '已撤回', value: DOC_STATUS.Withdrawn }
]

/** 审批中/已通过=锁定（后端 GuardEditable 同口径，这里只是不显示点不动的按钮） */
const editable = (row: OrderRow) =>
  row.status === DOC_STATUS.Draft || row.status === DOC_STATUS.Rejected || row.status === DOC_STATUS.Withdrawn

/* -------------------------------- 编辑弹窗 -------------------------------- */
const modalVisible = ref(false)
const readonly = ref(false)
const saving = ref(false)
const formRef = ref<FormInst | null>(null)
const editing = computed(() => !!form.id)

const form = reactive({
  id: null as string | null,
  version: 0,
  partnerId: null as string | null,
  orderDate: Date.now(),
  deliveryDate: null as number | null,
  extraId: null as string | null,
  remark: '',
  lines: [] as OrderLineForm[]
})
const formDocNo = ref('')

const rules: FormRules = {
  partnerId: [{ required: true, message: () => `请选择${kind.partnerLabel}`, trigger: ['change', 'blur'] }]
}

const dayMs = 86_400_000

function openCreate(): void {
  formDocNo.value = ''
  form.id = null
  form.version = 0
  form.partnerId = null
  form.orderDate = Date.now()
  form.deliveryDate = Date.now() + 7 * dayMs
  form.extraId = null
  form.remark = ''
  form.lines = [{ materialId: '', quantity: 1, unitPrice: 0, taxRate: 13, remark: null }]
  readonly.value = false
  modalVisible.value = true
}

async function openRow(row: OrderRow, mode: 'edit' | 'view'): Promise<void> {
  formDocNo.value = row.docNo
  let detail = row
  try {
    detail = await kind.detail(row.id) // 列表不带明细，打开时必须重取详情
  } catch {
    /* 拦截器已提示，退化成用列表行渲染 */
  }
  form.id = detail.id
  form.version = detail.version
  form.partnerId = kind.partnerIdOf(detail)
  form.orderDate = dayjs(detail.orderDate).valueOf()
  form.deliveryDate = detail.deliveryDate ? dayjs(detail.deliveryDate).valueOf() : null
  form.extraId = kind.extraIdOf(detail)
  form.remark = detail.remark ?? ''
  form.lines = detail.lines.map((l) => ({
    materialId: l.materialId,
    quantity: l.quantity,
    unitPrice: l.unitPrice,
    taxRate: l.taxRate,
    remark: l.remark ?? null
  }))
  readonly.value = mode === 'view'
  modalVisible.value = true
}

function toModel() {
  return {
    partnerId: form.partnerId ?? '',
    orderDate: dayjs(form.orderDate).format('YYYY-MM-DD'),
    deliveryDate: form.deliveryDate ? dayjs(form.deliveryDate).format('YYYY-MM-DD') : null,
    extraId: form.extraId,
    remark: form.remark.trim() || null,
    lines: form.lines,
    version: form.version
  }
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  if (!form.lines.length) {
    message.warning('请至少录入一行明细')
    return false
  }
  saving.value = true
  try {
    if (editing.value && form.id) await kind.update(form.id, toModel())
    else await kind.create(toModel())
    message.success(`${kind.title}已保存`)
    await list.load()
    return true
  } catch {
    return false // 物料不存在/数量非法等业务错误由拦截器弹出，弹窗保持打开
  } finally {
    saving.value = false
  }
}

async function remove(row: OrderRow): Promise<void> {
  try {
    await kind.remove(row.id)
    message.success('已删除')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

async function submitToFlow(row: OrderRow): Promise<void> {
  try {
    const result = await kind.submit(row.id)
    message.success(result.status === DOC_STATUS.Approved ? '已提交（该单据未绑定审批流，直通通过）' : '已提交审批')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

/* ---------------------------------- 表格 ---------------------------------- */
const columns = computed<DataTableColumns<OrderRow>>(() => [
  { title: '单号', key: 'docNo', width: 150 },
  { title: kind.partnerLabel, key: 'partner', minWidth: 170, ellipsis: { tooltip: true }, render: (row) => kind.partnerOf(row) },
  { title: '订单日期', key: 'orderDate', width: 116, render: (row) => formatDate(row.orderDate) },
  {
    title: '交货日期',
    key: 'deliveryDate',
    width: 116,
    render: (row) => (row.deliveryDate ? formatDate(row.deliveryDate) : h('span', { class: 'ps-muted' }, '-'))
  },
  {
    title: '合计数量',
    key: 'totalQty',
    width: 106,
    render: (row) => h('span', { style: 'font-variant-numeric: tabular-nums' }, String(row.totalQty))
  },
  {
    title: '合计金额',
    key: 'totalAmount',
    width: 126,
    render: (row) => h('b', { style: 'font-variant-numeric: tabular-nums' }, `¥${row.totalAmount.toFixed(2)}`)
  },
  {
    title: '明细',
    key: 'lines',
    width: 76,
    render: (row) => h('span', { class: 'ps-muted' }, `${row.lineCount} 行`)
  },
  {
    title: '状态',
    key: 'status',
    width: 92,
    render: (row) => {
      const meta = docStatusMeta(row.status)
      return h(NTag, { size: 'small', bordered: false, type: meta.type }, { default: () => meta.label })
    }
  },
  { title: '归属人', key: 'ownerUserName', width: 110 },
  {
    title: kind.extraLabel,
    key: 'extra',
    minWidth: 140,
    ellipsis: { tooltip: true },
    render: (row) => kind.extraOf(row) ?? h('span', { class: 'ps-muted' }, '-')
  },
  { title: '创建时间', key: 'createTime', width: 165, sorter: true, render: (row) => formatDateTime(row.createTime) },
  {
    title: '操作',
    key: 'actions',
    width: 178,
    fixed: 'right',
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm(`${kind.perm}:edit`) && editable(row)
            ? h(NButton, { size: 'tiny', text: true, type: 'primary', onClick: () => openRow(row, 'edit') }, { default: () => '编辑' })
            : h(NButton, { size: 'tiny', text: true, onClick: () => openRow(row, 'view') }, { default: () => '查看' }),
          hasPerm(`${kind.perm}:submit`) && editable(row)
            ? h(
                NPopconfirm,
                { onPositiveClick: () => submitToFlow(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'info' }, { default: () => '提交' }),
                  default: () => `提交「${row.docNo}」进入审批？未绑定流程时会直接通过。`
                }
              )
            : null,
          hasPerm(`${kind.perm}:delete`) && editable(row)
            ? h(
                NPopconfirm,
                { onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除「${row.docNo}」？明细一并软删。`
                }
              )
            : null
        ]
      })
  }
])

onMounted(() => void loadOptions())
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NForm inline :model="list.queryParams" label-placement="left" :show-feedback="false">
        <NSpace :size="12" align="center" wrap>
          <NFormItem label="关键字">
            <NInput
              v-model:value="list.queryParams.keyword"
              placeholder="单号 / 往来单位"
              clearable
              style="width: 210px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem :label="kind.partnerLabel">
            <NSelect
              v-model:value="list.queryParams.partnerId"
              :options="partnerOpts"
              filterable
              placeholder="全部"
              clearable
              style="width: 200px"
            />
          </NFormItem>
          <NFormItem label="状态">
            <NSelect
              v-model:value="list.queryParams.status"
              :options="STATUS_OPTIONS"
              placeholder="全部"
              clearable
              style="width: 120px"
            />
          </NFormItem>
          <NFormItem label="订单日期">
            <NDatePicker
              :value="rangeValue"
              type="daterange"
              clearable
              style="width: 250px"
              @update:value="onRangeUpdate"
            />
          </NFormItem>
          <NFormItem>
            <NCheckbox v-model:checked="list.queryParams.mine" @update:checked="list.search">只看我的</NCheckbox>
          </NFormItem>
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="onReset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" class="ps-scm__toolbar" :wrap="true">
        <NButton v-permission="`${kind.perm}:add`" type="primary" @click="openCreate">新增{{ kind.title }}</NButton>
        <span class="ps-muted">共 {{ list.total.value }} 张 · 合计金额与明细行金额由后端复核</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: OrderRow) => row.id"
        :scroll-x="1600"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="card"
      :title="readonly ? `${kind.title}详情 ${formDocNo}` : editing ? `编辑${kind.title}` : `新增${kind.title}`"
      style="width: 1120px; max-width: 96vw"
      :bordered="false"
      draggable
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="top" :show-feedback="!readonly">
        <NGrid :cols="24" :x-gap="12">
          <NGridItem :span="6">
            <NFormItem :label="kind.partnerLabel" path="partnerId">
              <NSelect
                v-model:value="form.partnerId"
                :options="partnerOpts"
                :disabled="readonly"
                filterable
                :placeholder="`选择${kind.partnerLabel}`"
              />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="5">
            <NFormItem label="订单日期" path="orderDate">
              <NDatePicker v-model:value="form.orderDate" type="date" :disabled="readonly" style="width: 100%" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="5">
            <NFormItem label="交货日期" path="deliveryDate">
              <NDatePicker v-model:value="form.deliveryDate" type="date" clearable :disabled="readonly" style="width: 100%" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="8">
            <NFormItem :label="kind.extraLabel" path="extraId">
              <NSelect
                v-model:value="form.extraId"
                :options="extraOpts"
                :disabled="readonly"
                filterable
                clearable
                placeholder="可选"
              />
            </NFormItem>
          </NGridItem>
        </NGrid>
        <OrderLinesEditor
          v-model="form.lines"
          :materials="materialOpts"
          :readonly="readonly"
          :quote="(materialId) => kind.quote(form.partnerId ?? '', materialId)"
        />
        <NFormItem label="备注" path="remark" style="margin-top: 12px">
          <NInput
            v-model:value="form.remark"
            type="textarea"
            :rows="2"
            maxlength="512"
            show-count
            :disabled="readonly"
            placeholder="交期、结算方式等"
          />
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="modalVisible = false">{{ readonly ? '关闭' : '取消' }}</NButton>
          <NButton v-if="!readonly" type="primary" :loading="saving" @click="submit">保存</NButton>
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
