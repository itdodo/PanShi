<script setup lang="ts">
import { computed, h, reactive, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
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
  createPosition,
  deletePosition,
  pagePositions,
  updatePosition,
  type PositionDto,
  type PositionForm
} from '@/api/admin'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'
import { STATUS_OPTIONS, statusTag } from '../_shared'

/** 岗位管理（/sys/position）：标准分页 CRUD，契约全部走 api/admin.ts（地址与后端一致）。 */
type PositionRow = PositionDto
type PositionQueryModel = { keyword: string; status: number | null }
type PositionFormModel = {
  id: string | null
  positionCode: string
  positionName: string
  sort: number
  status: number
  remark: string
  version: number
}

const list = usePageList<PositionRow, PositionQueryModel>({
  fetcher: pagePositions,
  defaultQuery: () => ({ keyword: '', status: null })
})

const modalVisible = ref(false)
const saving = ref(false)
const editing = computed(() => !!form.id)
const formRef = ref<FormInst | null>(null)

const form = reactive<PositionFormModel>({
  id: null,
  positionCode: '',
  positionName: '',
  sort: 10,
  status: 0,
  remark: '',
  version: 0
})

const rules: FormRules = {
  positionCode: [
    { required: true, max: 64, message: '请输入岗位编码', trigger: ['input', 'blur'] },
    { pattern: /^[A-Za-z][\w.-]*$/, message: '岗位编码需以字母开头', trigger: ['input', 'blur'] }
  ],
  positionName: [{ required: true, max: 64, message: '请输入岗位名称', trigger: ['input', 'blur'] }]
}

function resetForm(): void {
  form.id = null
  form.positionCode = ''
  form.positionName = ''
  form.sort = 10
  form.status = 0
  form.remark = ''
  form.version = 0
}

function openCreate(): void {
  resetForm()
  modalVisible.value = true
}

function openEdit(row: PositionRow): void {
  form.id = row.id
  form.positionCode = row.positionCode
  form.positionName = row.positionName
  form.sort = row.sort
  form.status = row.status
  form.remark = row.remark ?? ''
  form.version = row.version
  modalVisible.value = true
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  const payload: PositionForm = {
    positionCode: form.positionCode.trim(),
    positionName: form.positionName.trim(),
    sort: form.sort,
    status: form.status,
    remark: form.remark.trim() || null
  }
  try {
    if (editing.value && form.id) {
      await updatePosition(form.id, { ...payload, version: form.version })
      message.success('岗位已保存')
    } else {
      await createPosition(payload)
      message.success('岗位已新增')
    }
    await list.load()
    return true
  } catch {
    return false
  } finally {
    saving.value = false
  }
}

async function remove(row: PositionRow): Promise<void> {
  try {
    await deletePosition(row.id)
    message.success('已删除')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

const columns = computed<DataTableColumns<PositionRow>>(() => [
  { title: '岗位编码', key: 'positionCode', width: 160 },
  { title: '岗位名称', key: 'positionName', minWidth: 160 },
  { title: '排序', key: 'sort', width: 80 },
  {
    title: '状态',
    key: 'status',
    width: 90,
    render: (row) => {
      const tag = statusTag(row.status)
      return h(NTag, { size: 'small', bordered: false, type: tag.type }, { default: () => tag.label })
    }
  },
  { title: '备注', key: 'remark', minWidth: 180, ellipsis: { tooltip: true }, render: (row) => row.remark ?? '-' },
  { title: '创建时间', key: 'createTime', width: 170, render: (row) => formatDateTime(row.createTime) },
  {
    title: '操作',
    key: 'actions',
    width: 130,
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm('sys:position:edit')
            ? h(NButton, { size: 'tiny', text: true, type: 'primary', onClick: () => openEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('sys:position:delete')
            ? h(
                NPopconfirm,
                { onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除岗位「${row.positionName}」？已挂在用户上时后端会拒绝。`
                }
              )
            : null
        ]
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
              placeholder="岗位编码 / 名称"
              clearable
              style="width: 220px"
              @keyup.enter="list.search"
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
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="list.reset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" style="margin: 16px 0 12px">
        <NButton v-permission="'sys:position:add'" type="primary" @click="openCreate">新增岗位</NButton>
        <span class="ps-muted">共 {{ list.total.value }} 条</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: PositionRow) => row.id"
        :scroll-x="980"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑岗位' : '新增岗位'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 520px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
      @after-leave="resetForm"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="82">
        <NFormItem label="岗位编码" path="positionCode">
          <NInput v-model:value="form.positionCode" :disabled="editing" maxlength="64" placeholder="如 manager" />
        </NFormItem>
        <NFormItem label="岗位名称" path="positionName">
          <NInput v-model:value="form.positionName" maxlength="64" placeholder="如 部门经理" />
        </NFormItem>
        <NFormItem label="排序" path="sort">
          <NInputNumber v-model:value="form.sort" :min="0" :max="9999" style="width: 140px" />
        </NFormItem>
        <NFormItem label="状态" path="status">
          <NSwitch :value="form.status === 0" @update:value="(v: boolean) => (form.status = v ? 0 : 1)">
            <template #checked>正常</template>
            <template #unchecked>停用</template>
          </NSwitch>
        </NFormItem>
        <NFormItem label="备注" path="remark">
          <NInput v-model:value="form.remark" type="textarea" :rows="2" maxlength="512" show-count />
        </NFormItem>
      </NForm>
    </NModal>
  </div>
</template>
