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
  createMaterial,
  deleteMaterial,
  MD_DICT,
  pageMaterials,
  updateMaterial,
  type MaterialDto,
  type MaterialForm
} from '@/api/basedata'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'
import { MD_STATUS_OPTIONS, orMuted, renderPrice, renderStatusTag, useDict } from '../_shared'

/**
 * 物料管理（/md/material）：编码手填且软删过滤下唯一，分类与单位取字典。
 * 权限：basedata:material:list（菜单级）/ add / edit / delete。
 * 主数据全局共享，不按部门切——采购单据要能选到任意启用料号。
 */
const category = useDict(MD_DICT.materialCategory)
const unit = useDict(MD_DICT.unit)

type Row = MaterialDto
type QueryModel = { keyword: string; category: string | null; status: number | null }

const list = usePageList<Row, QueryModel>({
  fetcher: pageMaterials,
  defaultQuery: () => ({ keyword: '', category: null, status: null }),
  pageSize: 20
})

const modalVisible = ref(false)
const saving = ref(false)
const formRef = ref<FormInst | null>(null)
const editing = computed(() => !!form.id)

const form = reactive({
  id: null as string | null,
  materialCode: '',
  materialName: '',
  category: null as string | null,
  spec: '',
  unit: null as string | null,
  purchasePrice: null as number | null,
  salePrice: null as number | null,
  status: 0,
  remark: '',
  version: 0
})

const rules: FormRules = {
  materialCode: [
    { required: true, message: '请输入物料编码', trigger: ['input', 'blur'] },
    { min: 2, max: 32, message: '编码 2~32 个字符', trigger: ['input', 'blur'] }
  ],
  materialName: [{ required: true, max: 128, message: '请输入物料名称', trigger: ['input', 'blur'] }],
  spec: [{ max: 128, message: '规格最多 128 字', trigger: ['input', 'blur'] }],
  remark: [{ max: 512, message: '备注最多 512 字', trigger: ['input', 'blur'] }]
}

function openCreate(): void {
  form.id = null
  form.materialCode = ''
  form.materialName = ''
  form.category = null
  form.spec = ''
  form.unit = null
  form.purchasePrice = null
  form.salePrice = null
  form.status = 0
  form.remark = ''
  form.version = 0
  modalVisible.value = true
}

function openEdit(row: Row): void {
  form.id = row.id
  form.materialCode = row.materialCode
  form.materialName = row.materialName
  form.category = row.category ?? null
  form.spec = row.spec ?? ''
  form.unit = row.unit ?? null
  form.purchasePrice = row.purchasePrice ?? null
  form.salePrice = row.salePrice ?? null
  form.status = row.status
  form.remark = row.remark ?? ''
  form.version = row.version
  modalVisible.value = true
}

function toPayload(): MaterialForm {
  return {
    materialCode: form.materialCode.trim(),
    materialName: form.materialName.trim(),
    category: form.category,
    spec: form.spec.trim() || null,
    unit: form.unit,
    purchasePrice: form.purchasePrice,
    salePrice: form.salePrice,
    status: form.status,
    remark: form.remark.trim() || null
  }
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  try {
    if (editing.value && form.id) await updateMaterial(form.id, { ...toPayload(), version: form.version })
    else await createMaterial(toPayload())
    message.success(editing.value ? '物料已保存' : '物料已新增')
    await list.load()
    return true
  } catch {
    return false // 编码重复等业务错误由拦截器弹出，弹窗保持打开供修改
  } finally {
    saving.value = false
  }
}

async function remove(row: Row): Promise<void> {
  try {
    await deleteMaterial(row.id)
    message.success('已删除')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

const columns = computed<DataTableColumns<Row>>(() => [
  { title: '物料编码', key: 'materialCode', width: 150, sorter: true },
  { title: '名称', key: 'materialName', minWidth: 180, ellipsis: { tooltip: true } },
  {
    title: '分类',
    key: 'category',
    width: 110,
    render: (row) =>
      row.category
        ? h(NTag, { size: 'small', bordered: false }, { default: () => category.labelOf(row.category) })
        : orMuted(null)
  },
  { title: '规格型号', key: 'spec', minWidth: 140, ellipsis: { tooltip: true }, render: (row) => orMuted(row.spec) },
  { title: '单位', key: 'unit', width: 90, render: (row) => orMuted(unit.labelOf(row.unit)) },
  { title: '参考采购价', key: 'purchasePrice', width: 110, render: (row) => renderPrice(row.purchasePrice) },
  { title: '参考销售价', key: 'salePrice', width: 110, render: (row) => renderPrice(row.salePrice) },
  { title: '状态', key: 'status', width: 88, render: (row) => renderStatusTag(row.status) },
  { title: '备注', key: 'remark', minWidth: 160, ellipsis: { tooltip: true }, render: (row) => orMuted(row.remark) },
  { title: '创建时间', key: 'createTime', width: 165, sorter: true, render: (row) => formatDateTime(row.createTime) },
  {
    title: '操作',
    key: 'actions',
    width: 118,
    fixed: 'right',
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm('basedata:material:edit')
            ? h(NButton, { size: 'tiny', text: true, type: 'primary', onClick: () => openEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('basedata:material:delete')
            ? h(
                NPopconfirm,
                { onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除物料「${row.materialCode}」？软删后同编码可重新占用。`
                }
              )
            : null
        ]
      })
  }
])

onMounted(() => {
  void category.load()
  void unit.load()
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
              placeholder="编码 / 名称 / 规格"
              clearable
              style="width: 220px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="分类">
            <NSelect
              v-model:value="list.queryParams.category"
              :options="category.options.value"
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
        <NButton v-permission="'basedata:material:add'" type="primary" @click="openCreate">新增物料</NButton>
        <span class="ps-muted">共 {{ list.total.value }} 条</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: Row) => row.id"
        :scroll-x="1400"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑物料' : '新增物料'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 640px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="92" class="ps-md__form">
        <NGrid :cols="24" :x-gap="12">
          <NGridItem :span="12">
            <NFormItem label="物料编码" path="materialCode">
              <NInput v-model:value="form.materialCode" maxlength="32" placeholder="如 M-STEEL-01" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="名称" path="materialName">
              <NInput v-model:value="form.materialName" maxlength="128" placeholder="物料名称" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="分类" path="category">
              <NSelect v-model:value="form.category" :options="category.options.value" clearable placeholder="取字典" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="计量单位" path="unit">
              <NSelect v-model:value="form.unit" :options="unit.options.value" clearable placeholder="取字典" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="24">
            <NFormItem label="规格型号" path="spec">
              <NInput v-model:value="form.spec" maxlength="128" placeholder="如 Q235B 100×100×6000" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="采购价" path="purchasePrice">
              <NInputNumber v-model:value="form.purchasePrice" :min="0" :precision="2" clearable style="width: 100%" />
            </NFormItem>
          </NGridItem>
          <NGridItem :span="12">
            <NFormItem label="销售价" path="salePrice">
              <NInputNumber v-model:value="form.salePrice" :min="0" :precision="2" clearable style="width: 100%" />
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
