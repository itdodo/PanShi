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
import {
  createStockDoc,
  deleteStockDoc,
  getStockDoc,
  pageStockDocs,
  postStockDoc,
  STOCK_KINDS,
  STOCK_STATUS,
  stockKindLabel,
  stockStatusMeta,
  updateStockDoc,
  voidStockDoc,
  type MaterialChoice,
  type OrderLineForm,
  type StockDocDto,
  type StockDocForm
} from '@/api/scm'
import { pageMaterials, warehouseOptions } from '@/api/basedata'
import OrderLinesEditor from '@/components/OrderLinesEditor.vue'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDate, formatDateTime } from '@/utils/format'

/**
 * 出入库单（/scm/stock-doc）。草稿只是把单子写下来，**过账**才动库存；
 * 已过账不可改不可删，出错走「作废」（按本单流水逐条反向冲销）。
 * 盘点行的数量是「实盘数」，弹窗里会显示当前账面数与差异，过账时按差额记账。
 */
const KIND_OPTIONS: SelectOption[] = STOCK_KINDS.map((k) => ({ label: k.label, value: k.value }))
const STATUS_OPTIONS: SelectOption[] = [
  { label: '草稿', value: STOCK_STATUS.Draft },
  { label: '已过账', value: STOCK_STATUS.Posted },
  { label: '已作废', value: STOCK_STATUS.Void }
]

const warehouses = ref<SelectOption[]>([])
const materialOpts = ref<MaterialChoice[]>([])

type Row = StockDocDto
type QueryModel = {
  keyword: string
  kind: number | null
  status: number | null
  warehouseId: string | null
  mine: boolean
  begin?: string
  end?: string
}

const list = usePageList<Row, QueryModel>({
  fetcher: (q) =>
    pageStockDocs({
      pageNum: q.pageNum,
      pageSize: q.pageSize,
      sortField: q.sortField,
      sortOrder: q.sortOrder,
      keyword: q.keyword || undefined,
      kind: q.kind ?? undefined,
      status: q.status ?? undefined,
      warehouseId: q.warehouseId ?? undefined,
      mine: q.mine,
      begin: q.begin,
      end: q.end
    }),
  defaultQuery: () => ({
    keyword: '', kind: null, status: null, warehouseId: null, mine: false, begin: undefined, end: undefined
  }),
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

/* -------------------------------- 编辑弹窗 -------------------------------- */
const modalVisible = ref(false)
const readonly = ref(false)
const saving = ref(false)
const formRef = ref<FormInst | null>(null)
const editing = computed(() => !!form.id)
const isTransfer = computed(() => form.kind === 5)
const isCount = computed(() => form.kind === 6)

const form = reactive({
  id: null as string | null,
  version: 0,
  docNo: '',
  kind: 1,
  warehouseId: null as string | null,
  targetWarehouseId: null as string | null,
  bizDate: Date.now(),
  sourceOrderNo: '',
  remark: '',
  lines: [] as OrderLineForm[]
})

const rules: FormRules = {
  warehouseId: [{ required: true, message: '请选择仓库', trigger: ['change', 'blur'] }]
}

function blankLines(): OrderLineForm[] {
  return [{ materialId: '', quantity: 1, unitPrice: 0, taxRate: 0, remark: null }]
}

function openCreate(): void {
  form.id = null
  form.version = 0
  form.docNo = ''
  form.kind = 1
  form.warehouseId = null
  form.targetWarehouseId = null
  form.bizDate = Date.now()
  form.sourceOrderNo = ''
  form.remark = ''
  form.lines = blankLines()
  readonly.value = false
  modalVisible.value = true
}

async function openRow(row: Row, mode: 'edit' | 'view'): Promise<void> {
  let detail = row
  try {
    detail = await getStockDoc(row.id)
  } catch {
    /* 拦截器已提示，退化成用列表行渲染 */
  }
  form.id = detail.id
  form.version = detail.version
  form.docNo = detail.docNo
  form.kind = detail.kind
  form.warehouseId = detail.warehouseId
  form.targetWarehouseId = detail.targetWarehouseId ?? null
  form.bizDate = dayjs(detail.bizDate).valueOf()
  form.sourceOrderNo = detail.sourceOrderNo ?? ''
  form.remark = detail.remark ?? ''
  form.lines = detail.lines.map((l) => ({
    materialId: l.materialId, quantity: l.quantity, unitPrice: 0, taxRate: 0, remark: l.remark ?? null
  }))
  readonly.value = mode === 'view' || detail.status !== STOCK_STATUS.Draft
  modalVisible.value = true
}

function toPayload(): StockDocForm {
  return {
    kind: form.kind,
    warehouseId: form.warehouseId ?? '',
    targetWarehouseId: isTransfer.value ? form.targetWarehouseId ?? null : null,
    bizDate: dayjs(form.bizDate).format('YYYY-MM-DD'),
    sourceOrderNo: form.sourceOrderNo.trim() || null,
    remark: form.remark.trim() || null,
    lines: form.lines.map((l) => ({ materialId: l.materialId, quantity: l.quantity, remark: l.remark ?? null })),
    version: form.version
  }
}

/** preset="card" 的弹窗没有 dialog 的 positive-click，保存成功后要自己收起，否则再点一次就是重复建单 */
async function saveAndClose(): Promise<void> {
  if (await submit()) modalVisible.value = false
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
    if (editing.value && form.id) await updateStockDoc(form.id, toPayload())
    else await createStockDoc(toPayload())
    message.success('单据已保存（过账后才影响库存）')
    await list.load()
    return true
  } catch {
    return false
  } finally {
    saving.value = false
  }
}

async function act(row: Row, action: 'post' | 'void' | 'delete'): Promise<void> {
  try {
    if (action === 'post') {
      await postStockDoc(row.id)
      message.success(`「${row.docNo}」已过账，库存与流水已更新`)
    } else if (action === 'void') {
      await voidStockDoc(row.id)
      message.success(`「${row.docNo}」已作废，按原流水反向冲销`)
    } else {
      await deleteStockDoc(row.id)
      message.success('已删除')
    }
    await list.load()
  } catch {
    /* 库存不足等业务错误由拦截器弹出 */
  }
}

const columns = computed<DataTableColumns<Row>>(() => [
  { title: '单号', key: 'docNo', width: 150 },
  {
    title: '类型',
    key: 'kind',
    width: 104,
    render: (row) => h(NTag, { size: 'small', bordered: false }, { default: () => stockKindLabel(row.kind) })
  },
  { title: '仓库', key: 'warehouseName', minWidth: 130, ellipsis: { tooltip: true } },
  {
    title: '目标仓库',
    key: 'targetWarehouseName',
    minWidth: 130,
    ellipsis: { tooltip: true },
    render: (row) => row.targetWarehouseName ?? h('span', { class: 'ps-muted' }, '-')
  },
  { title: '业务日期', key: 'bizDate', width: 116, render: (row) => formatDate(row.bizDate) },
  {
    title: '合计数量',
    key: 'totalQty',
    width: 108,
    render: (row) => h('b', { style: 'font-variant-numeric: tabular-nums' }, String(row.totalQty))
  },
  { title: '明细', key: 'lineCount', width: 76, render: (row) => h('span', { class: 'ps-muted' }, `${row.lineCount} 行`) },
  {
    title: '状态',
    key: 'status',
    width: 92,
    render: (row) => {
      const meta = stockStatusMeta(row.status)
      return h(NTag, { size: 'small', bordered: false, type: meta.type }, { default: () => meta.label })
    }
  },
  {
    title: '来源单号',
    key: 'sourceOrderNo',
    width: 140,
    render: (row) => row.sourceOrderNo ?? h('span', { class: 'ps-muted' }, '-')
  },
  { title: '操作人', key: 'ownerUserName', width: 100 },
  { title: '过账时间', key: 'postedTime', width: 165, render: (row) => formatDateTime(row.postedTime) },
  {
    title: '操作',
    key: 'actions',
    width: 196,
    fixed: 'right',
    render: (row) =>
      h(NSpace, { size: 10 }, {
        // 每项带 key 并滤掉 null：状态一变（草稿→已过账）动作集就换一套，
        // 无 key 的空洞会让 NSpace 留下上一个弹层的触发器，行里多出个重复按钮
        default: () => [
          row.status === STOCK_STATUS.Draft && hasPerm('scm:stockdoc:edit')
            ? h(NButton, { key: 'edit', size: 'tiny', text: true, type: 'primary', onClick: () => openRow(row, 'edit') }, { default: () => '编辑' })
            : h(NButton, { key: 'view', size: 'tiny', text: true, onClick: () => openRow(row, 'view') }, { default: () => '查看' }),
          row.status === STOCK_STATUS.Draft && hasPerm('scm:stockdoc:post')
            ? h(
                NPopconfirm,
                { key: 'post', onPositiveClick: () => act(row, 'post') },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'success' }, { default: () => '过账' }),
                  default: () => `过账「${row.docNo}」？库存与流水立即变动，之后不可修改。`
                }
              )
            : null,
          row.status === STOCK_STATUS.Posted && hasPerm('scm:stockdoc:post')
            ? h(
                NPopconfirm,
                { key: 'void', onPositiveClick: () => act(row, 'void') },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'warning' }, { default: () => '作废' }),
                  default: () => `作废「${row.docNo}」？按本单流水逐条反向冲销，库存不足时会失败。`
                }
              )
            : null,
          row.status === STOCK_STATUS.Draft && hasPerm('scm:stockdoc:delete')
            ? h(
                NPopconfirm,
                { key: 'delete', onPositiveClick: () => act(row, 'delete') },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除草稿「${row.docNo}」？明细一并软删。`
                }
              )
            : null
        ].filter(Boolean)
      })
  }
])

onMounted(async () => {
  try {
    warehouses.value = await warehouseOptions()
  } catch {
    warehouses.value = []
  }
  try {
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
              placeholder="单号 / 仓库"
              clearable
              style="width: 190px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="类型">
            <NSelect v-model:value="list.queryParams.kind" :options="KIND_OPTIONS" placeholder="全部" clearable style="width: 130px" />
          </NFormItem>
          <NFormItem label="状态">
            <NSelect v-model:value="list.queryParams.status" :options="STATUS_OPTIONS" placeholder="全部" clearable style="width: 110px" />
          </NFormItem>
          <NFormItem label="仓库">
            <NSelect v-model:value="list.queryParams.warehouseId" :options="warehouses" placeholder="全部" clearable style="width: 170px" />
          </NFormItem>
          <NFormItem label="业务日期">
            <NDatePicker :value="rangeValue" type="daterange" clearable style="width: 250px" @update:value="onRangeUpdate" />
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
        <NButton v-permission="'scm:stockdoc:add'" type="primary" @click="openCreate">新增单据</NButton>
        <span class="ps-muted">共 {{ list.total.value }} 张 · 草稿不影响库存，过账才记账</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: Row) => row.id"
        :scroll-x="1680"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="card"
      :title="readonly ? `单据详情 ${form.docNo}` : editing ? '编辑单据' : '新增单据'"
      style="width: 980px; max-width: 96vw"
      :bordered="false"
      draggable
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="top" :show-feedback="!readonly">
        <NGrid :cols="24" :x-gap="12">
          <NGridItem :span="5">
            <NFormItem label="单据类型" path="kind">
              <NSelect v-model:value="form.kind" :options="KIND_OPTIONS" :disabled="readonly || editing" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="6">
            <NFormItem :label="isTransfer ? '源仓库' : '仓库'" path="warehouseId">
              <NSelect v-model:value="form.warehouseId" :options="warehouses" :disabled="readonly" filterable placeholder="选择仓库" />
            </NFormItem>
          </NGridItem>
          <NGridItem v-if="isTransfer" :span="6">
            <NFormItem label="目标仓库" path="targetWarehouseId">
              <NSelect v-model:value="form.targetWarehouseId" :options="warehouses" :disabled="readonly" filterable placeholder="选择目标仓库" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="6">
            <NFormItem label="业务日期" path="bizDate">
              <NDatePicker v-model:value="form.bizDate" type="date" :disabled="readonly" style="width: 100%" />
            </NFormItem>
          </NGridItem>
          <NGridItem v-if="!isTransfer" :span="6">
            <NFormItem label="来源单号" path="sourceOrderNo">
              <NInput v-model:value="form.sourceOrderNo" maxlength="32" :disabled="readonly" placeholder="如 PO20261001001" />
            </NFormItem>
          </NGridItem>
        </NGrid>

        <p v-if="isCount" class="ps-muted ps-scm__hint">
          明细填的是<em>实盘数</em>，过账时按「实盘 − 账面」记差额；差额为 0 的行不产生流水。
        </p>
        <OrderLinesEditor v-model="form.lines" :materials="materialOpts" :readonly="readonly" quantity-only />

        <NFormItem label="备注" path="remark" style="margin-top: 12px">
          <NInput
            v-model:value="form.remark"
            type="textarea"
            :rows="2"
            maxlength="512"
            show-count
            :disabled="readonly"
            placeholder="经手人、车牌、盘点范围等"
          />
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="modalVisible = false">{{ readonly ? '关闭' : '取消' }}</NButton>
          <NButton v-if="!readonly" type="primary" :loading="saving" @click="saveAndClose">保存</NButton>
        </NSpace>
      </template>
    </NModal>
  </div>
</template>

<style scoped>
.ps-scm__toolbar {
  margin: 16px 0 12px;
}

.ps-scm__hint {
  margin: 0 0 8px;
  font-size: 12px;
}

.ps-scm__hint em {
  font-style: normal;
  font-weight: 600;
}
</style>
