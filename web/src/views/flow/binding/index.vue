<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
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
  NSelect,
  NSpace,
  NSwitch,
  NTag,
  type DataTableColumns,
  type FormInst,
  type FormRules,
  type SelectOption
} from 'naive-ui'
import { deleteFlowBinding, listFlowBindings, pageFlowDefs, saveFlowBinding, type FlowBindingDto } from '@/api/flow'
import { message } from '@/utils/feedback'
import { isConflict } from '@/api/types'
import { bizTableLabel } from '@/components/flowEnums'
import { hasPerm } from '@/directives/permission'

/**
 * 单据 ↔ 流程绑定：一张业务表同一时刻只能绑一个启用流程，换绑即时生效（在途实例仍按原定义走完）。
 * saveFlowBinding 为 upsert（业务表无记录则新增），故新增/编辑共用一个弹窗。
 */
const BIZ_TABLE_CHOICES: SelectOption[] = [
  { label: '报销单（biz_expense）', value: 'biz_expense' },
  { label: '采购申请单（biz_purchase_request）', value: 'biz_purchase_request' }
]

const rows = ref<FlowBindingDto[]>([])
const loading = ref(false)
const flowOptions = ref<SelectOption[]>([])

async function loadList(): Promise<void> {
  loading.value = true
  try {
    rows.value = (await listFlowBindings()) ?? []
  } catch {
    rows.value = []
  } finally {
    loading.value = false
  }
}

async function loadFlows(): Promise<void> {
  if (!hasPerm('workflow:def:list')) return
  try {
    const page = await pageFlowDefs({ pageNum: 1, pageSize: 200, status: 1 })
    const seen = new Set<string>()
    const opts: SelectOption[] = []
    for (const def of page?.rows ?? []) {
      if (seen.has(def.flowCode)) continue
      seen.add(def.flowCode)
      opts.push({ label: `${def.flowName}（${def.flowCode}）`, value: def.flowCode })
    }
    flowOptions.value = opts
  } catch {
    flowOptions.value = []
  }
}

/* ---------------------------------- 编辑弹窗 --------------------------------- */
const showModal = ref(false)
const creating = ref(false)
const saving = ref(false)
const formRef = ref<FormInst | null>(null)
const form = reactive({ id: '', businessTable: '', flowCode: null as string | null, status: 1, remark: '', version: 0 })

const rules: FormRules = {
  businessTable: [{ required: true, message: '请选择业务表', trigger: ['input', 'change'] }],
  flowCode: [{ required: true, message: '请选择要绑定的启用流程', trigger: ['change'] }]
}

const modalTitle = computed(() => (creating.value ? '新增绑定' : `编辑绑定 · ${bizTableLabel(form.businessTable)}`))

function openCreate(): void {
  creating.value = true
  Object.assign(form, { id: '', businessTable: '', flowCode: null, status: 1, remark: '', version: 0 })
  showModal.value = true
  void loadFlows()
}

function openEdit(row: FlowBindingDto): void {
  creating.value = false
  Object.assign(form, {
    id: row.id,
    businessTable: row.businessTable,
    flowCode: row.flowCode,
    status: row.status,
    remark: row.remark ?? '',
    version: row.version
  })
  showModal.value = true
  void loadFlows()
}

async function save(): Promise<void> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return
  if (!form.flowCode) {
    message.warning('请选择流程')
    return
  }
  if (creating.value && rows.value.some((r) => r.businessTable === form.businessTable)) {
    message.warning('该业务表已有绑定，请直接编辑')
    return
  }
  saving.value = true
  try {
    await saveFlowBinding({
      businessTable: form.businessTable.trim(),
      flowCode: form.flowCode,
      status: form.status,
      remark: form.remark || null,
      version: form.version
    })
    message.success('绑定已保存')
    showModal.value = false
    await loadList()
  } catch (err) {
    if (isConflict(err)) await loadList()
  } finally {
    saving.value = false
  }
}

async function remove(row: FlowBindingDto): Promise<void> {
  try {
    await deleteFlowBinding(row.id)
    message.success('已解除绑定')
    await loadList()
  } catch {
    /* 拦截器已提示 */
  }
}

const columns: DataTableColumns<FlowBindingDto> = [
  { title: '业务表', key: 'businessTable', width: 200, render: (row) => `${bizTableLabel(row.businessTable)} / ${row.businessTable}` },
  { title: '流程编码', key: 'flowCode', width: 110 },
  {
    title: '流程名称',
    key: 'flowName',
    minWidth: 170,
    ellipsis: { tooltip: true },
    render: (row) => row.flowName || h('span', { class: 'ps-muted' }, '（流程已删除）')
  },
  {
    title: '状态',
    key: 'status',
    width: 96,
    render: (row) =>
      h(NTag, { size: 'small', bordered: false, type: row.status === 1 ? 'success' : 'default' }, { default: () => (row.status === 1 ? '启用' : '停用') })
  },
  {
    title: '备注',
    key: 'remark',
    minWidth: 160,
    ellipsis: { tooltip: true },
    render: (row) => row.remark || h('span', { class: 'ps-muted' }, '—')
  },
  {
    title: '操作',
    key: 'actions',
    width: 150,
    render: (row) => {
      if (!hasPerm('workflow:binding:edit')) return h('span', { class: 'ps-muted' }, '只读')
      return h(NSpace, { size: 6, wrap: false }, {
        default: () => [
          h(NButton, { size: 'tiny', type: 'primary', tertiary: true, onClick: () => openEdit(row) }, { default: () => '编辑' }),
          h(
            NPopconfirm,
            { onPositiveClick: () => remove(row) },
            {
              trigger: () => h(NButton, { size: 'tiny', type: 'error', tertiary: true }, { default: () => '删除' }),
              default: () => `解绑后 ${bizTableLabel(row.businessTable)} 提交将不走审批（直通），确认删除？`
            }
          )
        ]
      })
    }
  }
]

onMounted(() => {
  void loadList()
  void loadFlows()
})
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NSpace justify="space-between" align="center" class="flow-binding__bar">
        <NAlert type="info" :bordered="false" class="flow-binding__tip" style="margin: 0">
          可选流程 = 各编码的「启用」版本；同编码新版本启用后，此处按流程编码自动跟随。
        </NAlert>
        <NSpace>
          <NButton :loading="loading" @click="loadList">刷新</NButton>
          <NButton v-permission="'workflow:binding:edit'" type="primary" @click="openCreate">新增绑定</NButton>
        </NSpace>
      </NSpace>

      <NDataTable
        size="small"
        :columns="columns"
        :data="rows"
        :loading="loading"
        :row-key="(row: FlowBindingDto) => row.id"
        :scroll-x="900"
      />
    </NCard>

    <NModal
      v-model:show="showModal"
      preset="card"
      :title="modalTitle"
      :style="{ width: '520px' }"
      :mask-closable="false"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="90">
        <NFormItem label="业务表" path="businessTable">
          <NSelect
            v-model:value="form.businessTable"
            :options="BIZ_TABLE_CHOICES"
            :disabled="!creating"
            filterable
            tag
            placeholder="选择业务表（样板：报销单 / 采购申请单）"
          />
        </NFormItem>
        <NFormItem label="绑定流程" path="flowCode">
          <NSelect
            v-model:value="form.flowCode"
            :options="flowOptions"
            filterable
            clearable
            placeholder="选择启用中的流程定义"
          />
        </NFormItem>
        <NFormItem label="启用">
          <NSwitch v-model:value="form.status" :checked-value="1" :unchecked-value="0" />
          <span class="ps-muted" style="margin-left: 10px">停用=该单据提交不走审批</span>
        </NFormItem>
        <NFormItem label="备注">
          <NInput v-model:value="form.remark" type="textarea" :rows="2" maxlength="512" placeholder="选填" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="showModal = false">取消</NButton>
          <NButton type="primary" :loading="saving" @click="save">保存</NButton>
        </NSpace>
      </template>
    </NModal>
  </div>
</template>

<style scoped>
.flow-binding__bar {
  margin-bottom: 12px;
  align-items: center;
}

.flow-binding__tip {
  flex: 1 1 auto;
}
</style>
