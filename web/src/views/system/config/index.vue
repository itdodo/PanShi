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
  NModal,
  NPopconfirm,
  NSpace,
  NTag,
  type DataTableColumns,
  type FormInst,
  type FormRules
} from 'naive-ui'
import { put } from '@/api/http'
import { createConfig, deleteConfig, pageConfigs, type ConfigDto } from '@/api/system/config'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { toNum } from '../_shared'

/**
 * 参数设置（/sys/config）：分页 + CRUD。
 * 后端 ConfigDto = { id, configName, configKey, configValue, builtIn, remark, version }；
 * 新增 POST /sys/config、编辑 PUT /sys/config/{id}（必带 version）、删除 DELETE /sys/config/{id}。
 * ⚠️ 内置参数（builtIn=true）禁止删除；参数键创建后不可改（后端 UpdateAsync 直接拒绝改键）。
 */
type ConfigRow = ConfigDto
type ConfigQueryModel = { keyword: string }
type ConfigFormModel = {
  id: string | null
  configName: string
  configKey: string
  configValue: string
  remark: string
  version: number
}

const list = usePageList<ConfigRow, ConfigQueryModel>({
  fetcher: pageConfigs,
  defaultQuery: () => ({ keyword: '' })
})

const modalVisible = ref(false)
const saving = ref(false)
const editing = computed(() => !!form.id)
const formRef = ref<FormInst | null>(null)

const form = reactive<ConfigFormModel>({
  id: null,
  configName: '',
  configKey: '',
  configValue: '',
  remark: '',
  version: 0
})

const rules: FormRules = {
  configName: [{ required: true, max: 64, message: '请输入参数名称', trigger: ['input', 'blur'] }],
  configKey: [
    { required: true, max: 128, message: '请输入参数键', trigger: ['input', 'blur'] },
    { pattern: /^[a-zA-Z][\w.-]*$/, message: '参数键需以字母开头，可含 . - _', trigger: ['input', 'blur'] }
  ]
}

function resetForm(): void {
  form.id = null
  form.configName = ''
  form.configKey = ''
  form.configValue = ''
  form.remark = ''
  form.version = 0
}

function openCreate(): void {
  resetForm()
  modalVisible.value = true
}

function openEdit(row: ConfigRow): void {
  form.id = row.id
  form.configName = row.configName
  form.configKey = row.configKey
  form.configValue = row.configValue ?? ''
  form.remark = row.remark ?? ''
  form.version = row.version
  modalVisible.value = true
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  const payload = {
    configName: form.configName.trim(),
    configKey: form.configKey.trim(),
    configValue: form.configValue,
    remark: form.remark.trim() || null
  }
  try {
    if (editing.value && form.id) {
      await put(`/sys/config/${form.id}`, { ...payload, version: form.version })
      message.success('参数已保存')
    } else {
      await createConfig(payload)
      message.success('参数已新增')
    }
    await list.load()
    return true
  } catch {
    return false
  } finally {
    saving.value = false
  }
}

async function remove(row: ConfigRow): Promise<void> {
  try {
    await deleteConfig(row.id)
    message.success('已删除')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

const columns = computed<DataTableColumns<ConfigRow>>(() => [
  { title: '参数名称', key: 'configName', minWidth: 150, ellipsis: { tooltip: true } },
  {
    title: '参数键',
    key: 'configKey',
    minWidth: 190,
    ellipsis: { tooltip: true },
    render: (row) => h('code', { class: 'ps-config-key' }, row.configKey)
  },
  { title: '参数值', key: 'configValue', minWidth: 170, ellipsis: { tooltip: true }, render: (row) => row.configValue ?? '-' },
  {
    title: '内置',
    key: 'builtIn',
    width: 84,
    render: (row) =>
      row.builtIn
        ? h(NTag, { size: 'small', bordered: false, type: 'warning' }, { default: () => '内置' })
        : h(NTag, { size: 'small', bordered: false }, { default: () => '自定义' })
  },
  { title: '备注', key: 'remark', minWidth: 160, ellipsis: { tooltip: true }, render: (row) => row.remark ?? '-' },
  { title: '版本', key: 'version', width: 66, render: (row) => String(toNum(row.version)) },
  {
    title: '操作',
    key: 'actions',
    width: 130,
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm('sys:config:edit')
            ? h(NButton, { size: 'tiny', text: true, type: 'primary', onClick: () => openEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('sys:config:delete') && !row.builtIn
            ? h(
                NPopconfirm,
                { onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  description: () => `删除参数「${row.configName}」？`
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
              placeholder="参数名称 / 键"
              clearable
              style="width: 240px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="list.reset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" style="margin: 16px 0 12px">
        <NButton v-permission="'sys:config:add'" type="primary" @click="openCreate">新增参数</NButton>
        <span class="ps-muted">共 {{ list.total.value }} 条 · 内置参数不可删除、键不可改</span>
      </NSpace>

      <NAlert type="info" :bordered="false" style="margin-bottom: 12px">
        内置参数（sys.captcha.enabled、sys.pwd.*、sys.user.initPassword 等）由后端各功能读取，改动即时生效，请谨慎。
      </NAlert>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: ConfigRow) => row.id"
        :scroll-x="1000"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑参数' : '新增参数'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 560px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
      @after-leave="resetForm"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="82">
        <NFormItem label="参数名称" path="configName">
          <NInput v-model:value="form.configName" maxlength="64" placeholder="如 验证码开关" />
        </NFormItem>
        <NFormItem label="参数键" path="configKey">
          <NInput
            v-model:value="form.configKey"
            :disabled="editing"
            maxlength="128"
            placeholder="如 sys.captcha.enabled"
          />
        </NFormItem>
        <NFormItem label="参数值" path="configValue">
          <NInput v-model:value="form.configValue" type="textarea" :rows="2" maxlength="1024" show-count />
        </NFormItem>
        <NFormItem label="备注" path="remark">
          <NInput v-model:value="form.remark" type="textarea" :rows="2" maxlength="512" show-count />
        </NFormItem>
      </NForm>
    </NModal>
  </div>
</template>

<style scoped>
.ps-config-key {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--ps-primary);
}
</style>
