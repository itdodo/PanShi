<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
  NForm,
  NFormItem,
  NGrid,
  NGridItem,
  NInput,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NSwitch,
  NTag,
  type DataTableColumns,
  type FormInst,
  type FormRules
} from 'naive-ui'
import {
  createCustomer,
  deleteCustomer,
  MD_DICT,
  pageCustomers,
  updateCustomer,
  type CustomerDto,
  type CustomerForm
} from '@/api/basedata'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'
import { MD_STATUS_OPTIONS, orMuted, renderStatusTag, useDict } from '../_shared'

/**
 * 客户管理（/md/customer）。编码手填且软删过滤下唯一；等级取字典 md_customer_level。
 * 权限：basedata:customer:list / add / edit / delete。
 */
const level = useDict(MD_DICT.customerLevel)

type Row = CustomerDto
type QueryModel = { keyword: string; level: string | null; status: number | null }

const list = usePageList<Row, QueryModel>({
  fetcher: pageCustomers,
  defaultQuery: () => ({ keyword: '', level: null, status: null }),
  pageSize: 20
})

const modalVisible = ref(false)
const saving = ref(false)
const formRef = ref<FormInst | null>(null)
const editing = computed(() => !!form.id)

const form = reactive({
  id: null as string | null,
  customerCode: '',
  customerName: '',
  shortName: '',
  taxNo: '',
  contact: '',
  phone: '',
  email: '',
  address: '',
  level: null as string | null,
  status: 0,
  remark: '',
  version: 0
})

const rules: FormRules = {
  customerCode: [
    { required: true, message: '请输入客户编码', trigger: ['input', 'blur'] },
    { min: 2, max: 32, message: '编码 2~32 个字符', trigger: ['input', 'blur'] }
  ],
  customerName: [{ required: true, max: 128, message: '请输入客户名称', trigger: ['input', 'blur'] }],
  email: [{ max: 64, message: '邮箱最多 64 字', trigger: ['input', 'blur'] }],
  remark: [{ max: 512, message: '备注最多 512 字', trigger: ['input', 'blur'] }]
}

function openCreate(): void {
  form.id = null
  form.customerCode = ''
  form.customerName = ''
  form.shortName = ''
  form.taxNo = ''
  form.contact = ''
  form.phone = ''
  form.email = ''
  form.address = ''
  form.level = null
  form.status = 0
  form.remark = ''
  form.version = 0
  modalVisible.value = true
}

function openEdit(row: Row): void {
  form.id = row.id
  form.customerCode = row.customerCode
  form.customerName = row.customerName
  form.shortName = row.shortName ?? ''
  form.taxNo = row.taxNo ?? ''
  form.contact = row.contact ?? ''
  form.phone = row.phone ?? ''
  form.email = row.email ?? ''
  form.address = row.address ?? ''
  form.level = row.level ?? null
  form.status = row.status
  form.remark = row.remark ?? ''
  form.version = row.version
  modalVisible.value = true
}

function toPayload(): CustomerForm {
  const text = (v: string) => v.trim() || null
  return {
    customerCode: form.customerCode.trim(),
    customerName: form.customerName.trim(),
    shortName: text(form.shortName),
    taxNo: text(form.taxNo),
    contact: text(form.contact),
    phone: text(form.phone),
    email: text(form.email),
    address: text(form.address),
    level: form.level,
    status: form.status,
    remark: text(form.remark)
  }
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  try {
    if (editing.value && form.id) await updateCustomer(form.id, { ...toPayload(), version: form.version })
    else await createCustomer(toPayload())
    message.success(editing.value ? '客户已保存' : '客户已新增')
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
    await deleteCustomer(row.id)
    message.success('已删除')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

const columns = computed<DataTableColumns<Row>>(() => [
  { title: '客户编码', key: 'customerCode', width: 140, sorter: true },
  { title: '名称', key: 'customerName', minWidth: 180, ellipsis: { tooltip: true } },
  { title: '简称', key: 'shortName', width: 120, render: (row) => orMuted(row.shortName) },
  {
    title: '等级',
    key: 'level',
    width: 96,
    render: (row) =>
      row.level ? h(NTag, { size: 'small', bordered: false }, { default: () => level.labelOf(row.level) }) : orMuted(null)
  },
  { title: '联系人', key: 'contact', width: 100, render: (row) => orMuted(row.contact) },
  { title: '电话', key: 'phone', width: 130, render: (row) => orMuted(row.phone) },
  { title: '统一社会信用代码', key: 'taxNo', width: 180, render: (row) => orMuted(row.taxNo) },
  { title: '状态', key: 'status', width: 88, render: (row) => renderStatusTag(row.status) },
  { title: '地址', key: 'address', minWidth: 180, ellipsis: { tooltip: true }, render: (row) => orMuted(row.address) },
  { title: '创建时间', key: 'createTime', width: 165, sorter: true, render: (row) => formatDateTime(row.createTime) },
  {
    title: '操作',
    key: 'actions',
    width: 118,
    fixed: 'right',
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm('basedata:customer:edit')
            ? h(NButton, { key: 'edit', size: 'tiny', text: true, type: 'primary', onClick: () => openEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('basedata:customer:delete')
            ? h(
                NPopconfirm,
                { key: 'delete', onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除客户「${row.customerName}」？软删后同编码可重新占用。`
                }
              )
            : null
        ].filter(Boolean)
      })
  }
])

onMounted(() => void level.load())
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NForm inline :model="list.queryParams" label-placement="left" :show-feedback="false">
        <NSpace :size="12" align="center" wrap>
          <NFormItem label="关键字">
            <NInput
              v-model:value="list.queryParams.keyword"
              placeholder="编码 / 名称 / 简称 / 联系人"
              clearable
              style="width: 240px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="等级">
            <NSelect
              v-model:value="list.queryParams.level"
              :options="level.options.value"
              placeholder="全部"
              clearable
              style="width: 140px"
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
        <NButton v-permission="'basedata:customer:add'" type="primary" @click="openCreate">新增客户</NButton>
        <span class="ps-muted">共 {{ list.total.value }} 条</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: Row) => row.id"
        :scroll-x="1480"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑客户' : '新增客户'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 720px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="102" class="ps-md__form">
        <NGrid :cols="24" :x-gap="12">
          <NGridItem :span="12">
            <NFormItem label="客户编码" path="customerCode">
              <NInput v-model:value="form.customerCode" maxlength="32" placeholder="如 C-0001" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="名称" path="customerName">
              <NInput v-model:value="form.customerName" maxlength="128" placeholder="营业执照全称" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="简称" path="shortName">
              <NInput v-model:value="form.shortName" maxlength="64" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="等级" path="level">
              <NSelect v-model:value="form.level" :options="level.options.value" clearable placeholder="取字典" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="信用代码" path="taxNo">
              <NInput v-model:value="form.taxNo" maxlength="32" placeholder="18 位，自动转大写" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="联系人" path="contact">
              <NInput v-model:value="form.contact" maxlength="32" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="电话" path="phone">
              <NInput v-model:value="form.phone" maxlength="32" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="邮箱" path="email">
              <NInput v-model:value="form.email" maxlength="64" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="24">
            <NFormItem label="地址" path="address">
              <NInput v-model:value="form.address" maxlength="256" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="24">
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
</style>
