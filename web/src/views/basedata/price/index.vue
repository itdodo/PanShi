<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import dayjs from 'dayjs'
import {
  NButton,
  NCard,
  NDatePicker,
  NDataTable,
  NForm,
  NFormItem,
  NGrid,
  NGridItem,
  NInput,
  NInputNumber,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NSwitch,
  type DataTableColumns,
  type FormInst,
  type FormRules,
  type SelectOption
} from 'naive-ui'
import {
  createPriceAgreement,
  deletePriceAgreement,
  pagePriceAgreements,
  updatePriceAgreement,
  type PriceAgreementDto,
  type PriceAgreementForm
} from '@/api/basedata'
import { materialOptions, supplierOptions } from '@/api/basedata'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'
import { MD_STATUS_OPTIONS, orMuted, renderStatusTag } from '../_shared'

/**
 * 采购价目表（/md/price-agreement）：供应商 × 物料一行现行协议价。
 * 调价直接改这行，历史价格走「变更日志」回溯，不在业务表里堆版本行。
 * 订单页选料时按这行带出单价与税率（只认启用且在生效区间内的）。
 */
type Row = PriceAgreementDto
type QueryModel = { keyword: string; supplierId: string | null; status: number | null }

const suppliers = ref<SelectOption[]>([])
const materials = ref<SelectOption[]>([])

const list = usePageList<Row, QueryModel>({
  fetcher: pagePriceAgreements,
  defaultQuery: () => ({ keyword: '', supplierId: null, status: null }),
  pageSize: 20
})

const modalVisible = ref(false)
const saving = ref(false)
const formRef = ref<FormInst | null>(null)
const editing = computed(() => !!form.id)

const form = reactive({
  id: null as string | null,
  supplierId: null as string | null,
  materialId: null as string | null,
  unitPrice: 0,
  taxRate: 13,
  begin: null as number | null,
  end: null as number | null,
  status: 0,
  remark: '',
  version: 0
})

const rules: FormRules = {
  supplierId: [{ required: true, message: '请选择供应商', trigger: ['change', 'blur'] }],
  materialId: [{ required: true, message: '请选择物料', trigger: ['change', 'blur'] }]
}

function openCreate(): void {
  form.id = null
  form.supplierId = null
  form.materialId = null
  form.unitPrice = 0
  form.taxRate = 13
  form.begin = null
  form.end = null
  form.status = 0
  form.remark = ''
  form.version = 0
  modalVisible.value = true
}

function openEdit(row: Row): void {
  form.id = row.id
  form.supplierId = row.supplierId
  form.materialId = row.materialId
  form.unitPrice = row.unitPrice
  form.taxRate = row.taxRate
  form.begin = row.beginDate ? dayjs(row.beginDate).valueOf() : null
  form.end = row.endDate ? dayjs(row.endDate).valueOf() : null
  form.status = row.status
  form.remark = row.remark ?? ''
  form.version = row.version
  modalVisible.value = true
}

function toPayload(): PriceAgreementForm {
  return {
    supplierId: form.supplierId ?? '',
    materialId: form.materialId ?? '',
    unitPrice: form.unitPrice,
    taxRate: form.taxRate,
    beginDate: form.begin ? dayjs(form.begin).format('YYYY-MM-DD') : null,
    endDate: form.end ? dayjs(form.end).format('YYYY-MM-DD') : null,
    status: form.status,
    remark: form.remark.trim() || null
  }
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  try {
    if (editing.value && form.id) await updatePriceAgreement(form.id, { ...toPayload(), version: form.version })
    else await createPriceAgreement(toPayload())
    message.success(editing.value ? '协议价已保存' : '协议价已新增')
    await list.load()
    return true
  } catch {
    return false
  } finally {
    saving.value = false
  }
}

async function remove(row: Row): Promise<void> {
  try {
    await deletePriceAgreement(row.id)
    message.success('已删除')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

const columns = computed<DataTableColumns<Row>>(() => [
  { title: '供应商', key: 'supplierName', minWidth: 180, ellipsis: { tooltip: true } },
  { title: '物料编码', key: 'materialCode', width: 140 },
  { title: '物料名称', key: 'materialName', minWidth: 150, ellipsis: { tooltip: true } },
  {
    title: '含税单价',
    key: 'unitPrice',
    width: 116,
    render: (row) => h('span', { style: 'font-variant-numeric: tabular-nums' }, row.unitPrice.toFixed(4))
  },
  { title: '税率%', key: 'taxRate', width: 88, render: (row) => row.taxRate.toFixed(2) },
  {
    title: '生效起',
    key: 'beginDate',
    width: 120,
    render: (row) => orMuted(row.beginDate ? dayjs(row.beginDate).format('YYYY-MM-DD') : '')
  },
  {
    title: '失效止',
    key: 'endDate',
    width: 120,
    render: (row) =>
      row.endDate
        ? h('span', { class: dayjs(row.endDate).isBefore(dayjs(), 'day') ? 'ps-price-expired' : '' },
            dayjs(row.endDate).format('YYYY-MM-DD'))
        : h('span', { class: 'ps-muted' }, '长期')
  },
  { title: '状态', key: 'status', width: 88, render: (row) => renderStatusTag(row.status) },
  { title: '备注', key: 'remark', minWidth: 150, ellipsis: { tooltip: true }, render: (row) => orMuted(row.remark) },
  { title: '创建时间', key: 'createTime', width: 165, sorter: true, render: (row) => formatDateTime(row.createTime) },
  {
    title: '操作',
    key: 'actions',
    width: 118,
    fixed: 'right',
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm('basedata:price:edit')
            ? h(NButton, { size: 'tiny', text: true, type: 'primary', onClick: () => openEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('basedata:price:delete')
            ? h(
                NPopconfirm,
                { onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除「${row.supplierName} × ${row.materialName}」的协议价？`
                }
              )
            : null
        ]
      })
  }
])

onMounted(async () => {
  try {
    suppliers.value = await supplierOptions()
    materials.value = await materialOptions()
  } catch {
    /* 下拉取不到不挡住列表 */
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
              placeholder="供应商 / 物料编码 / 名称"
              clearable
              style="width: 240px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="供应商">
            <NSelect
              v-model:value="list.queryParams.supplierId"
              :options="suppliers"
              filterable
              placeholder="全部"
              clearable
              style="width: 220px"
            />
          </NFormItem>
          <NFormItem label="状态">
            <NSelect
              v-model:value="list.queryParams.status"
              :options="MD_STATUS_OPTIONS"
              placeholder="全部"
              clearable
              style="width: 120px"
            />
          </NFormItem>
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="list.reset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" class="ps-md__toolbar" :wrap="true">
        <NButton v-permission="'basedata:price:add'" type="primary" @click="openCreate">新增协议价</NButton>
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
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑协议价' : '新增协议价'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 660px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="92" class="ps-md__form">
        <NGrid :cols="24" :x-gap="12">
          <NGridItem :span="12">
            <NFormItem label="供应商" path="supplierId">
              <NSelect v-model:value="form.supplierId" :options="suppliers" filterable placeholder="选择供应商" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="物料" path="materialId">
              <NSelect v-model:value="form.materialId" :options="materials" filterable placeholder="选择物料" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="含税单价" path="unitPrice">
              <NInputNumber v-model:value="form.unitPrice" :min="0" :precision="4" style="width: 100%" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="税率%" path="taxRate">
              <NInputNumber v-model:value="form.taxRate" :min="0" :max="100" :precision="2" style="width: 100%" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="生效起" path="begin">
              <NDatePicker v-model:value="form.begin" type="date" clearable style="width: 100%" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="失效止" path="end">
              <NDatePicker v-model:value="form.end" type="date" clearable style="width: 100%" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="状态" path="status">
              <NSwitch :value="form.status === 0" @update:value="(v: boolean) => (form.status = v ? 0 : 1)">
                <template #checked>启用</template>
                <template #unchecked>停用</template>
              </NSwitch>
            </NFormItem>
          </NGridItem>
          <NGridItem :span="24">
            <NFormItem label="备注" path="remark">
              <NInput v-model:value="form.remark" type="textarea" :rows="2" maxlength="512" show-count />
            </NFormItem>
          </NGridItem>
        </NGrid>
      </NForm>
    </NModal>
  </div>
</template>

<style scoped>
.ps-md__toolbar {
  margin: 16px 0 12px;
}

.ps-md__form {
  margin-top: 6px;
}

.ps-price-expired {
  color: var(--ps-text-3);
  text-decoration: line-through;
}
</style>
