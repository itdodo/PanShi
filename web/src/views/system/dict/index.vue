<script setup lang="ts">
import { computed, h, reactive, ref } from 'vue'
import {
  NAlert,
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
  createDictItem,
  createDictType,
  deleteDictItem,
  deleteDictType,
  getDictDataByType,
  pageDictTypes,
  updateDictItem,
  updateDictType,
  type DictItemDto,
  type DictTypeDto
} from '@/api/system/dict'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { STATUS_OPTIONS, TAG_TYPE_OPTIONS, idForApi, statusTag, tagTypeOf, toNum } from '../_shared'

/**
 * 字典管理（/sys/dict）：左「字典类型」分页表 + 右「字典数据项」表（选中类型后加载
 * GET /sys/dict/data/type/{typeId}），两栏各自 CRUD。
 * 后端 DTO：DictTypeDto{dictName,dictCode,remark,version} / DictDataDto{dictTypeId,label,value,sort,status,tagType,isDefault,version}
 * ——视图按该真实契约直调 http（api/system/dict.ts 的声明已同步为同一字段名，接入与否另议）。
 */
type DictTypeRow = DictTypeDto

type DictDataRow = DictItemDto

type TypeQueryModel = { keyword: string }

/* -------------------------------- 左侧：类型 -------------------------------- */
const typeList = usePageList<DictTypeRow, TypeQueryModel>({
  fetcher: pageDictTypes,
  defaultQuery: () => ({ keyword: '' }),
  pageSize: 10
})

const selectedType = ref<DictTypeRow | null>(null)

const typeColumns = computed<DataTableColumns<DictTypeRow>>(() => [
  { title: '名称', key: 'dictName', minWidth: 110, ellipsis: { tooltip: true } },
  { title: '编码', key: 'dictCode', minWidth: 120, ellipsis: { tooltip: true } },
  {
    title: '操作',
    key: 'actions',
    width: 118,
    render: (row) =>
      h(NSpace, { size: 8 }, {
        default: () => [
          hasPerm('sys:dict:edit')
            ? h(NButton, { key: 'edit', size: 'tiny', text: true, type: 'primary', onClick: () => openTypeEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('sys:dict:delete')
            ? h(
                NPopconfirm,
                { key: 'delete', onPositiveClick: () => removeType(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除类型「${row.dictName}」及其数据项？`
                }
              )
            : null
        ].filter(Boolean)
      })
  }
])

function selectType(row: DictTypeRow): void {
  selectedType.value = row
  void loadDataItems(row.id)
}

/** 单击行即选中（编辑/删除按钮自身点击不冒泡到行） */
function typeRowProps(row: DictTypeRow) {
  return {
    style: 'cursor: pointer;',
    onClick: () => selectType(row)
  }
}

const typeModalVisible = ref(false)
const typeSaving = ref(false)
const typeEditing = computed(() => !!typeForm.id)
const typeFormRef = ref<FormInst | null>(null)

const typeForm = reactive({
  id: null as string | null,
  dictName: '',
  dictCode: '',
  remark: '',
  version: 0
})

const typeRules: FormRules = {
  dictName: [{ required: true, max: 64, message: '请输入字典名称', trigger: ['input', 'blur'] }],
  dictCode: [
    { required: true, max: 64, message: '请输入字典编码', trigger: ['input', 'blur'] },
    { pattern: /^[a-z][\w]*$/, message: '编码需小写字母开头（前端按编码取项：/sys/dict/data/{code}）', trigger: ['input', 'blur'] }
  ]
}

function openTypeCreate(): void {
  typeForm.id = null
  typeForm.dictName = ''
  typeForm.dictCode = ''
  typeForm.remark = ''
  typeForm.version = 0
  typeModalVisible.value = true
}

function openTypeEdit(row: DictTypeRow): void {
  typeForm.id = row.id
  typeForm.dictName = row.dictName
  typeForm.dictCode = row.dictCode
  typeForm.remark = row.remark ?? ''
  typeForm.version = row.version
  typeModalVisible.value = true
}

async function submitType(): Promise<boolean> {
  const invalid = await typeFormRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  typeSaving.value = true
  const payload = {
    dictName: typeForm.dictName.trim(),
    dictCode: typeForm.dictCode.trim(),
    remark: typeForm.remark.trim() || null
  }
  try {
    if (typeEditing.value && typeForm.id) {
      await updateDictType(typeForm.id, { ...payload, version: typeForm.version })
      message.success('字典类型已保存')
    } else {
      await createDictType(payload)
      message.success('字典类型已新增')
    }
    await typeList.load()
    return true
  } catch {
    return false
  } finally {
    typeSaving.value = false
  }
}

async function removeType(row: DictTypeRow): Promise<void> {
  try {
    await deleteDictType(row.id)
    if (selectedType.value?.id === row.id) {
      selectedType.value = null
      dataItems.value = []
    }
    message.success('已删除')
    await typeList.load()
  } catch {
    /* 已提示 */
  }
}

/* -------------------------------- 右侧：数据项 -------------------------------- */
const dataItems = ref<DictDataRow[]>([])
const dataLoading = ref(false)

async function loadDataItems(typeId: string): Promise<void> {
  dataLoading.value = true
  try {
    dataItems.value = (await getDictDataByType(typeId)) ?? []
  } catch {
    dataItems.value = []
  } finally {
    dataLoading.value = false
  }
}

const dataColumns = computed<DataTableColumns<DictDataRow>>(() => [
  {
    title: '标签',
    key: 'label',
    minWidth: 130,
    render: (row) =>
      h(NTag, { size: 'small', bordered: false, type: tagTypeOf(row.tagType) }, { default: () => row.label })
  },
  { title: '值', key: 'value', minWidth: 110, ellipsis: { tooltip: true } },
  { title: '排序', key: 'sort', width: 76 },
  {
    title: '默认',
    key: 'isDefault',
    width: 70,
    render: (row) =>
      row.isDefault ? h(NTag, { size: 'small', bordered: false, type: 'success' }, { default: () => '默认' }) : h('span', { class: 'ps-muted' }, '—')
  },
  {
    title: '状态',
    key: 'status',
    width: 80,
    render: (row) => {
      const tag = statusTag(row.status)
      return h(NTag, { size: 'small', bordered: false, type: tag.type }, { default: () => tag.label })
    }
  },
  {
    title: '操作',
    key: 'actions',
    width: 118,
    render: (row) =>
      h(NSpace, { size: 8 }, {
        default: () => [
          hasPerm('sys:dict:edit')
            ? h(NButton, { key: 'edit', size: 'tiny', text: true, type: 'primary', onClick: () => openDataEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('sys:dict:delete')
            ? h(
                NPopconfirm,
                { key: 'delete', onPositiveClick: () => removeDataItem(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除数据项「${row.label}」？`
                }
              )
            : null
        ].filter(Boolean)
      })
  }
])

const dataModalVisible = ref(false)
const dataSaving = ref(false)
const dataEditing = computed(() => !!dataForm.id)
const dataFormRef = ref<FormInst | null>(null)

const dataForm = reactive({
  id: null as string | null,
  label: '',
  value: '',
  sort: 1,
  status: 0,
  tagType: 'default',
  isDefault: false,
  version: 0
})

const dataRules: FormRules = {
  label: [{ required: true, max: 128, message: '请输入标签', trigger: ['input', 'blur'] }],
  value: [{ required: true, max: 128, message: '请输入值', trigger: ['input', 'blur'] }]
}

function openDataCreate(): void {
  dataForm.id = null
  dataForm.label = ''
  dataForm.value = ''
  dataForm.sort = toNum(dataItems.value.length) + 1
  dataForm.status = 0
  dataForm.tagType = 'default'
  dataForm.isDefault = false
  dataForm.version = 0
  dataModalVisible.value = true
}

function openDataEdit(row: DictDataRow): void {
  dataForm.id = row.id
  dataForm.label = row.label
  dataForm.value = row.value
  dataForm.sort = row.sort
  dataForm.status = row.status
  dataForm.tagType = tagTypeOf(row.tagType)
  dataForm.isDefault = !!row.isDefault
  dataForm.version = row.version
  dataModalVisible.value = true
}

async function submitDataItem(): Promise<boolean> {
  const type = selectedType.value
  if (!type) {
    message.warning('请先选择左侧字典类型')
    return false
  }
  const invalid = await dataFormRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  dataSaving.value = true
  const payload = {
    dictTypeId: idForApi(type.id) ?? 0,
    label: dataForm.label.trim(),
    value: dataForm.value.trim(),
    sort: dataForm.sort,
    status: dataForm.status,
    tagType: dataForm.tagType || null,
    isDefault: dataForm.isDefault
  }
  try {
    if (dataEditing.value && dataForm.id) {
      await updateDictItem(dataForm.id, { ...payload, version: dataForm.version })
      message.success('数据项已保存')
    } else {
      await createDictItem(payload)
      message.success('数据项已新增')
    }
    await loadDataItems(type.id)
    return true
  } catch {
    return false
  } finally {
    dataSaving.value = false
  }
}

async function removeDataItem(row: DictDataRow): Promise<void> {
  const type = selectedType.value
  try {
    await deleteDictItem(row.id)
    message.success('已删除')
    if (type) await loadDataItems(type.id)
  } catch {
    /* 已提示 */
  }
}

function refreshDataItems(): void {
  const type = selectedType.value
  if (type) void loadDataItems(type.id)
}
</script>

<template>
  <div class="ps-page">
    <div class="ps-dict">
      <NCard :bordered="false" class="ps-dict__left" title="字典类型">
        <template #header-extra>
          <NButton v-permission="'sys:dict:add'" size="small" type="primary" @click="openTypeCreate">新增类型</NButton>
        </template>

        <NSpace :size="8" style="margin-bottom: 12px">
          <NInput
            v-model:value="typeList.queryParams.keyword"
            placeholder="名称 / 编码"
            clearable
            style="width: 160px"
            @keyup.enter="typeList.search"
          />
          <NButton type="primary" size="small" @click="typeList.search">查询</NButton>
          <NButton tertiary size="small" @click="typeList.reset">重置</NButton>
        </NSpace>

        <NDataTable
          remote
          :columns="typeColumns"
          :data="typeList.data.value"
          :loading="typeList.loading.value"
          :pagination="typeList.pagination.value"
          :row-key="(row: DictTypeRow) => row.id"
          :row-props="typeRowProps"
          :row-class-name="(row: DictTypeRow) => (row.id === selectedType?.id ? 'ps-dict__row--active' : '')"
          size="small"
          :bordered="false"
          flex-height
          style="height: 420px"
        />
      </NCard>

      <NCard :bordered="false" class="ps-dict__right" :title="selectedType ? `数据项 · ${selectedType.dictName}（${selectedType.dictCode}）` : '数据项'">
        <template #header-extra>
          <NSpace :size="8">
            <NButton
              v-permission="'sys:dict:add'"
              size="small"
              type="primary"
              :disabled="!selectedType"
              @click="openDataCreate"
            >
              新增数据项
            </NButton>
            <NButton size="small" tertiary :disabled="!selectedType" @click="refreshDataItems"> 刷新 </NButton>
          </NSpace>
        </template>

        <NAlert v-if="!selectedType" type="info" :bordered="false">
          单击左侧任一行即可加载该类型的数据项。
        </NAlert>
        <NDataTable
          v-else
          :columns="dataColumns"
          :data="dataItems"
          :loading="dataLoading"
          :row-key="(row: DictDataRow) => row.id"
          :pagination="false"
          size="small"
          :bordered="false"
          :scroll-x="700"
          flex-height
          style="height: 420px"
        />
      </NCard>
    </div>

    <NModal
      v-model:show="typeModalVisible"
      preset="dialog"
      :title="typeEditing ? '编辑字典类型' : '新增字典类型'"
      :positive-text="typeEditing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 520px"
      :positive-button-props="{ loading: typeSaving }"
      @positive-click="submitType"
    >
      <NForm ref="typeFormRef" :model="typeForm" :rules="typeRules" label-placement="left" label-width="82">
        <NFormItem label="字典名称" path="dictName">
          <NInput v-model:value="typeForm.dictName" maxlength="64" placeholder="如 性别" />
        </NFormItem>
        <NFormItem label="字典编码" path="dictCode">
          <NInput
            v-model:value="typeForm.dictCode"
            :disabled="typeEditing"
            maxlength="64"
            placeholder="如 sys_user_gender（业务侧按编码取项）"
          />
        </NFormItem>
        <NFormItem label="备注" path="remark">
          <NInput v-model:value="typeForm.remark" type="textarea" :rows="2" maxlength="512" show-count />
        </NFormItem>
      </NForm>
    </NModal>

    <NModal
      v-model:show="dataModalVisible"
      preset="dialog"
      :title="dataEditing ? '编辑数据项' : '新增数据项'"
      :positive-text="dataEditing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 520px"
      :positive-button-props="{ loading: dataSaving }"
      @positive-click="submitDataItem"
    >
      <NForm ref="dataFormRef" :model="dataForm" :rules="dataRules" label-placement="left" label-width="82">
        <NFormItem label="标签" path="label">
          <NInput v-model:value="dataForm.label" maxlength="128" placeholder="展示文案，如 男" />
        </NFormItem>
        <NFormItem label="值" path="value">
          <NInput v-model:value="dataForm.value" maxlength="128" placeholder="落库值，如 1" />
        </NFormItem>
        <NFormItem label="标签色" path="tagType">
          <NSelect v-model:value="dataForm.tagType" :options="TAG_TYPE_OPTIONS" placeholder="Naive tag type" />
        </NFormItem>
        <NFormItem label="排序" path="sort">
          <NInputNumber v-model:value="dataForm.sort" :min="0" :max="9999" style="width: 140px" />
        </NFormItem>
        <NFormItem label="默认项" path="isDefault">
          <NSwitch v-model:value="dataForm.isDefault" />
        </NFormItem>
        <NFormItem label="状态" path="status">
          <NSwitch :value="dataForm.status === 0" @update:value="(v: boolean) => (dataForm.status = v ? 0 : 1)">
            <template #checked>正常</template>
            <template #unchecked>停用</template>
          </NSwitch>
        </NFormItem>
      </NForm>
    </NModal>
  </div>
</template>

<style scoped>
.ps-dict {
  display: flex;
  gap: 14px;
  align-items: flex-start;
  flex-wrap: wrap;
}

.ps-dict__left {
  flex: 0 0 380px;
  min-width: 320px;
}

.ps-dict__right {
  flex: 1 1 520px;
  min-width: 360px;
}

:deep(.ps-dict__row--active) {
  background: var(--ps-primary-soft);
}
</style>
