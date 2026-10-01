<script setup lang="ts">
import { computed, h, reactive, ref } from 'vue'
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
  createWarehouse,
  deleteWarehouse,
  pageWarehouses,
  updateWarehouse,
  type WarehouseDto,
  type WarehouseForm
} from '@/api/basedata'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'
import { MD_STATUS_OPTIONS, orMuted, renderStatusTag } from '../_shared'

/**
 * 仓库管理（/md/warehouse）。默认仓全库至多一个，由后端在写入时互斥维护，
 * 所以这里勾了新仓、保存后要重查列表才能看到旧仓的勾被取消——不做前端假联动。
 */
type Row = WarehouseDto
type QueryModel = { keyword: string; status: number | null }

const list = usePageList<Row, QueryModel>({
  fetcher: pageWarehouses,
  defaultQuery: () => ({ keyword: '', status: null }),
  pageSize: 20
})

const modalVisible = ref(false)
const saving = ref(false)
const formRef = ref<FormInst | null>(null)
const editing = computed(() => !!form.id)

const form = reactive({
  id: null as string | null,
  warehouseCode: '',
  warehouseName: '',
  address: '',
  contact: '',
  phone: '',
  isDefault: false,
  status: 0,
  remark: '',
  version: 0
})

const rules: FormRules = {
  warehouseCode: [
    { required: true, message: '请输入仓库编码', trigger: ['input', 'blur'] },
    { min: 2, max: 32, message: '编码 2~32 个字符', trigger: ['input', 'blur'] }
  ],
  warehouseName: [{ required: true, max: 128, message: '请输入仓库名称', trigger: ['input', 'blur'] }]
}

function openCreate(): void {
  form.id = null
  form.warehouseCode = ''
  form.warehouseName = ''
  form.address = ''
  form.contact = ''
  form.phone = ''
  form.isDefault = false
  form.status = 0
  form.remark = ''
  form.version = 0
  modalVisible.value = true
}

function openEdit(row: Row): void {
  form.id = row.id
  form.warehouseCode = row.warehouseCode
  form.warehouseName = row.warehouseName
  form.address = row.address ?? ''
  form.contact = row.contact ?? ''
  form.phone = row.phone ?? ''
  form.isDefault = row.isDefault
  form.status = row.status
  form.remark = row.remark ?? ''
  form.version = row.version
  modalVisible.value = true
}

function toPayload(): WarehouseForm {
  const text = (v: string) => v.trim() || null
  return {
    warehouseCode: form.warehouseCode.trim(),
    warehouseName: form.warehouseName.trim(),
    address: text(form.address),
    contact: text(form.contact),
    phone: text(form.phone),
    isDefault: form.isDefault,
    status: form.status,
    remark: text(form.remark)
  }
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  try {
    if (editing.value && form.id) await updateWarehouse(form.id, { ...toPayload(), version: form.version })
    else await createWarehouse(toPayload())
    message.success(editing.value ? '仓库已保存' : '仓库已新增')
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
    await deleteWarehouse(row.id)
    message.success('已删除')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

const columns = computed<DataTableColumns<Row>>(() => [
  { title: '仓库编码', key: 'warehouseCode', width: 140, sorter: true },
  { title: '名称', key: 'warehouseName', minWidth: 160, ellipsis: { tooltip: true } },
  {
    title: '默认',
    key: 'isDefault',
    width: 80,
    render: (row) =>
      row.isDefault ? h(NTag, { size: 'small', bordered: false, type: 'info' }, { default: () => '默认仓' }) : orMuted(null)
  },
  { title: '负责人', key: 'contact', width: 100, render: (row) => orMuted(row.contact) },
  { title: '电话', key: 'phone', width: 130, render: (row) => orMuted(row.phone) },
  { title: '状态', key: 'status', width: 88, render: (row) => renderStatusTag(row.status) },
  { title: '地址', key: 'address', minWidth: 200, ellipsis: { tooltip: true }, render: (row) => orMuted(row.address) },
  { title: '创建时间', key: 'createTime', width: 165, sorter: true, render: (row) => formatDateTime(row.createTime) },
  {
    title: '操作',
    key: 'actions',
    width: 118,
    fixed: 'right',
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm('basedata:warehouse:edit')
            ? h(NButton, { key: 'edit', size: 'tiny', text: true, type: 'primary', onClick: () => openEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('basedata:warehouse:delete')
            ? h(
                NPopconfirm,
                { key: 'delete', onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除仓库「${row.warehouseName}」？软删后同编码可重新占用。`
                }
              )
            : null
        ].filter(Boolean)
      })
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
              placeholder="编码 / 名称"
              clearable
              style="width: 220px"
              @keyup.enter="list.search"
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
        <NButton v-permission="'basedata:warehouse:add'" type="primary" @click="openCreate">新增仓库</NButton>
        <span class="ps-muted">共 {{ list.total.value }} 条</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: Row) => row.id"
        :scroll-x="1250"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑仓库' : '新增仓库'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 640px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="92" class="ps-md__form">
        <NGrid :cols="24" :x-gap="12">
          <NGridItem :span="12">
            <NFormItem label="仓库编码" path="warehouseCode">
              <NInput v-model:value="form.warehouseCode" maxlength="32" placeholder="如 WH-01" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="名称" path="warehouseName">
              <NInput v-model:value="form.warehouseName" maxlength="128" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="负责人" path="contact">
              <NInput v-model:value="form.contact" maxlength="32" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="电话" path="phone">
              <NInput v-model:value="form.phone" maxlength="32" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="24">
            <NFormItem label="地址" path="address">
              <NInput v-model:value="form.address" maxlength="256" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="默认仓" path="isDefault">
              <NSwitch v-model:value="form.isDefault" />
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
</style>
