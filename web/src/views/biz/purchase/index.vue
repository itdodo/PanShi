<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import {
  NButton,
  NCard,
  NCheckbox,
  NDataTable,
  NDrawer,
  NDrawerContent,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NSpin,
  NTag,
  NUpload,
  type DataTableColumns,
  type FormInst,
  type FormRules,
  type SelectOption,
  type UploadCustomRequestOptions,
  type UploadFileInfo
} from 'naive-ui'
import {
  createPurchase,
  deletePurchase,
  DOC_STATUS,
  getPurchase,
  pagePurchases,
  submitPurchase,
  updatePurchase,
  uploadFile,
  type FileDto,
  type PurchaseDto
} from '@/api/biz'
import { instanceDetail, type FlowInstanceDetail } from '@/api/flow'
import { userOptions } from '@/api/admin'
import { usePageList } from '@/composables/usePageList'
import { useNoticeStore } from '@/stores/notice'
import { formatDateTime } from '@/utils/format'
import { message } from '@/utils/feedback'
import { hasPerm } from '@/directives/permission'
import FlowTimeline from '@/components/FlowTimeline.vue'
import BizDocPreview from '@/components/BizDocPreview.vue'
import { docStatusMeta } from '@/components/flowEnums'
import { probeSubmitterChoice } from '@/components/flowChoice'

/** 采购申请单：与报销单同构（品名/数量/预算金额/事由），提交走绑定流程。 */
const route = useRoute()
const notice = useNoticeStore()

const list = usePageList<PurchaseDto, { keyword: string; status: number | null; mine: boolean }>({
  fetcher: pagePurchases,
  defaultQuery: () => ({ keyword: '', status: null, mine: false }),
  immediate: false
})
const { queryParams, loading, data, pagination, search, reset, load } = list

/** DOC_STATUS 含 0（草稿），NSelect 不接受 null：下拉值统一 +1，0 表示「全部」 */
const STATUS_CHOICES: SelectOption[] = [
  { label: '全部', value: 0 },
  { label: '草稿', value: DOC_STATUS.Draft + 1 },
  { label: '审批中', value: DOC_STATUS.Running + 1 },
  { label: '通过', value: DOC_STATUS.Approved + 1 },
  { label: '拒绝', value: DOC_STATUS.Rejected + 1 },
  { label: '撤回', value: DOC_STATUS.Withdrawn + 1 }
]

function pickStatus(raw: unknown): void {
  const value = Number(raw ?? 0)
  queryParams.status = value === 0 ? null : value - 1
  void search()
}

const statusFilterValue = computed<number>(() => (queryParams.status === null ? 0 : queryParams.status + 1))

/* ---------------------------------- 编辑弹窗 --------------------------------- */
const showForm = ref(false)
const saving = ref(false)
const editingId = ref('')
const formVersion = ref(0)
const formRef = ref<FormInst | null>(null)
const form = reactive({ itemName: '', quantity: null as number | null, amount: null as number | null, reason: '' })
const fileList = ref<UploadFileInfo[]>([])

const rules: FormRules = {
  itemName: [{ required: true, message: '请输入采购品名', trigger: ['input', 'blur'] }],
  quantity: [
    {
      required: true,
      validator: (_rule: unknown, value: number | null) =>
        value !== null && value > 0 ? true : new Error('请输入大于 0 的数量'),
      trigger: ['input', 'blur']
    }
  ],
  amount: [
    {
      required: true,
      validator: (_rule: unknown, value: number | null) =>
        value !== null && value > 0 ? true : new Error('请输入大于 0 的预算金额'),
      trigger: ['input', 'blur']
    }
  ],
  reason: [{ required: true, message: '请填写采购事由', trigger: ['input', 'blur'] }]
}

const editable = (row: PurchaseDto): boolean =>
  row.status === DOC_STATUS.Draft || row.status === DOC_STATUS.Rejected || row.status === DOC_STATUS.Withdrawn

function openCreate(): void {
  editingId.value = ''
  formVersion.value = 0
  Object.assign(form, { itemName: '', quantity: 1, amount: null, reason: '' })
  fileList.value = []
  showForm.value = true
}

function openEdit(row: PurchaseDto): void {
  editingId.value = row.id
  formVersion.value = row.version
  Object.assign(form, {
    itemName: row.itemName,
    quantity: Number(row.quantity ?? 1),
    amount: Number(row.amount ?? 0),
    reason: row.reason ?? ''
  })
  fileList.value = (row.attachmentIds ?? []).map((id) => ({ id, name: `附件 ${id.slice(-6)}`, status: 'finished' }))
  showForm.value = true
}

async function customUpload({ file, onFinish, onError }: UploadCustomRequestOptions): Promise<void> {
  const raw = file.file
  if (!raw) {
    onError()
    return
  }
  try {
    const dto = await uploadFile<FileDto>('/file/upload', raw)
    file.id = dto.id
    file.name = dto.name
    file.url = dto.url
    onFinish()
  } catch {
    onError()
  }
}

const attachmentIds = computed<string[]>(() =>
  fileList.value.filter((f) => f.status === 'finished').map((f) => String(f.id))
)

async function saveDoc(): Promise<void> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return
  const payload = {
    itemName: form.itemName.trim(),
    quantity: Number(form.quantity ?? 0),
    amount: Number(form.amount ?? 0),
    reason: form.reason.trim() || null,
    attachmentIds: attachmentIds.value,
    version: formVersion.value
  }
  saving.value = true
  try {
    if (editingId.value) {
      await updatePurchase(editingId.value, payload)
      message.success('已保存草稿')
    } else {
      await createPurchase(payload)
      message.success('已新增草稿')
    }
    showForm.value = false
    await load()
  } catch {
    /* 拦截器已提示（409 冲突亦已弹） */
  } finally {
    saving.value = false
  }
}

async function removeDoc(row: PurchaseDto): Promise<void> {
  try {
    await deletePurchase(row.id)
    message.success('已删除草稿')
    await load()
  } catch {
    /* 拦截器已提示 */
  }
}

/* -------------------------------- 提交审批（选人） ------------------------------- */
const showChoice = ref(false)
const submitting = ref(false)
const choiceRequired = ref(false)
const choiceUsers = ref<string[]>([])
const choiceDocId = ref('')
const userOpts = ref<SelectOption[]>([])

async function openSubmit(row: PurchaseDto): Promise<void> {
  choiceDocId.value = row.id
  choiceUsers.value = []
  const need = await probeSubmitterChoice('biz_purchase_request')
  choiceRequired.value = need === 'yes'
  if (need === 'no') {
    await doSubmit()
    return
  }
  showChoice.value = true
  if (!userOpts.value.length) {
    try {
      userOpts.value = (await userOptions()).map((o) => ({ label: o.label, value: o.value }))
    } catch {
      userOpts.value = []
    }
  }
}

async function doSubmit(): Promise<void> {
  if (choiceRequired.value && !choiceUsers.value.length) {
    message.warning('该流程含发起人自选节点，请至少选择一位审批人')
    return
  }
  submitting.value = true
  try {
    await submitPurchase(choiceDocId.value, choiceUsers.value)
    message.success('已提交审批')
    showChoice.value = false
    await Promise.all([load(), notice.refreshCount()])
  } catch {
    /* 拦截器已提示 */
  } finally {
    submitting.value = false
  }
}

/* ---------------------------------- 预览 / 进度 --------------------------------- */
const showPreview = ref(false)
const previewId = ref('')
const previewRow = computed<PurchaseDto | null>(() => data.value.find((r) => r.id === previewId.value) ?? null)

const showProgress = ref(false)
const progressLoading = ref(false)
const progressDetail = ref<FlowInstanceDetail | null>(null)

function openPreview(row: PurchaseDto): void {
  previewId.value = row.id
  showPreview.value = true
}

async function openProgress(instanceId: string): Promise<void> {
  showProgress.value = true
  progressDetail.value = null
  progressLoading.value = true
  try {
    progressDetail.value = await instanceDetail(instanceId)
  } catch {
    /* 拦截器已提示 */
  } finally {
    progressLoading.value = false
  }
}

/* ----------------------------------- 列定义 ----------------------------------- */
const columns: DataTableColumns<PurchaseDto> = [
  { title: '申请单号', key: 'docNo', width: 170 },
  { title: '申请人', key: 'ownerUserName', width: 100, ellipsis: { tooltip: true } },
  { title: '采购品名', key: 'itemName', minWidth: 160, ellipsis: { tooltip: true } },
  {
    title: '数量',
    key: 'quantity',
    width: 84,
    align: 'right',
    render: (row) => h('span', { style: 'font-variant-numeric: tabular-nums' }, `${Number(row.quantity ?? 0)}`)
  },
  {
    title: '预算金额',
    key: 'amount',
    width: 124,
    align: 'right',
    render: (row) => h('span', { style: 'font-variant-numeric: tabular-nums' }, `¥${Number(row.amount ?? 0).toFixed(2)}`)
  },
  {
    title: '事由',
    key: 'reason',
    minWidth: 180,
    ellipsis: { tooltip: true },
    render: (row) => row.reason || h('span', { class: 'ps-muted' }, '—')
  },
  {
    title: '状态',
    key: 'status',
    width: 96,
    render: (row) => {
      const meta = docStatusMeta(row.status)
      return h(NTag, { size: 'small', bordered: false, type: meta.type }, { default: () => meta.label })
    }
  },
  { title: '附件', key: 'attachmentIds', width: 66, render: (row) => `${row.attachmentIds?.length ?? 0}` },
  { title: '申请时间', key: 'createTime', width: 168, render: (row) => formatDateTime(row.createTime) },
  {
    title: '操作',
    key: 'actions',
    width: 224,
    fixed: 'right',
    render: (row) => {
      const btns = [h(NButton, { size: 'tiny', tertiary: true, onClick: () => openPreview(row) }, { default: () => '预览' })]
      if (editable(row) && hasPerm('biz:purchase:edit')) {
        btns.push(h(NButton, { size: 'tiny', type: 'primary', tertiary: true, onClick: () => openEdit(row) }, { default: () => '编辑' }))
      }
      if (editable(row) && hasPerm('biz:purchase:submit')) {
        btns.push(
          h(NButton, { size: 'tiny', type: 'info', tertiary: true, onClick: () => openSubmit(row) }, { default: () => '提交审批' })
        )
      }
      if (editable(row) && hasPerm('biz:purchase:delete')) {
        btns.push(
          h(
            NPopconfirm,
            { onPositiveClick: () => removeDoc(row) },
            {
              trigger: () => h(NButton, { size: 'tiny', type: 'error', tertiary: true }, { default: () => '删除' }),
              default: () => `确认删除 ${row.docNo}？`
            }
          )
        )
      }
      return h(
        'div',
        { style: 'display:flex;gap:6px;flex-wrap:nowrap', onClick: (e: MouseEvent) => e.stopPropagation() },
        btns
      )
    }
  }
]

function rowProps(row: PurchaseDto) {
  return { style: 'cursor: pointer', onClick: () => openPreview(row) }
}

/* ------------------------- 从「我发起的」跳来改单重提 ------------------------- */
async function applyRouteQuery(): Promise<void> {
  const id = typeof route.query.id === 'string' ? route.query.id : ''
  if (!id) return
  const hit = data.value.find((r) => r.id === id)
  if (hit) {
    if (editable(hit)) openEdit(hit)
    else openPreview(hit)
    return
  }
  try {
    const doc = await getPurchase(id)
    if (doc && editable(doc)) openEdit(doc)
    else if (doc) {
      previewId.value = doc.id
      showPreview.value = true
    }
  } catch {
    /* 拦截器已提示 */
  }
}

onMounted(async () => {
  await search()
  await applyRouteQuery()
})
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NSpace justify="space-between" align="end" :wrap="false" class="biz-purchase__bar">
        <NForm inline :model="queryParams" @submit.prevent="search">
          <NFormItem label="关键字" path="keyword">
            <NInput
              v-model:value="queryParams.keyword"
              placeholder="单号 / 品名 / 事由"
              clearable
              style="width: 220px"
              @keyup.enter="search"
            />
          </NFormItem>
          <NFormItem label="状态" path="status">
            <NSelect :value="statusFilterValue" :options="STATUS_CHOICES" style="width: 120px" @update:value="pickStatus" />
          </NFormItem>
          <NFormItem path="mine">
            <NCheckbox v-model:checked="queryParams.mine" @update:checked="search">只看我的单据</NCheckbox>
          </NFormItem>
        </NForm>
        <NSpace>
          <NButton @click="reset">重置</NButton>
          <NButton :loading="loading" @click="search">查询</NButton>
          <NButton v-permission="'biz:purchase:add'" type="primary" @click="openCreate">新增采购申请</NButton>
        </NSpace>
      </NSpace>

      <NDataTable
        remote
        size="small"
        :columns="columns"
        :data="data"
        :loading="loading"
        :pagination="pagination"
        :row-key="(row: PurchaseDto) => row.id"
        :row-props="rowProps"
        :scroll-x="1440"
      />
    </NCard>

    <!-- 新增/编辑 -->
    <NModal
      v-model:show="showForm"
      preset="card"
      :title="editingId ? '编辑采购申请单' : '新增采购申请单'"
      :style="{ width: '560px' }"
      :mask-closable="false"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="86">
        <NFormItem label="采购品名" path="itemName">
          <NInput v-model:value="form.itemName" maxlength="128" placeholder="如：联想笔记本电脑" />
        </NFormItem>
        <NFormItem label="数量" path="quantity">
          <NInputNumber v-model:value="form.quantity" :precision="0" :min="1" :step="1" placeholder="1" style="width: 100%" />
        </NFormItem>
        <NFormItem label="预算金额" path="amount">
          <NInputNumber v-model:value="form.amount" :precision="2" :min="0" :step="100" placeholder="0.00" style="width: 100%">
            <template #prefix>¥</template>
          </NInputNumber>
        </NFormItem>
        <NFormItem label="采购事由" path="reason">
          <NInput v-model:value="form.reason" type="textarea" :rows="3" maxlength="500" show-count placeholder="用途与必要性说明" />
        </NFormItem>
        <NFormItem label="附件">
          <NUpload v-model:file-list="fileList" :custom-request="customUpload" :max="5" multiple accept="*/*">
            <NButton size="small" tertiary>选择文件（≤5 个）</NButton>
          </NUpload>
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="showForm = false">取消</NButton>
          <NButton type="primary" :loading="saving" @click="saveDoc">保存草稿</NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 提交审批：自选审批人 -->
    <NModal
      v-model:show="showChoice"
      preset="card"
      title="提交审批"
      :style="{ width: '480px' }"
      :mask-closable="false"
    >
      <NForm label-placement="left" label-width="96">
        <NFormItem label="自选审批人" :required="choiceRequired">
          <NSelect
            v-model:value="choiceUsers"
            :options="userOpts"
            multiple
            filterable
            clearable
            placeholder="该流程含「发起人自选」节点"
          />
        </NFormItem>
        <p class="ps-muted biz-purchase__hint">
          {{
            choiceRequired
              ? '该流程包含发起人自选审批节点，至少选择一位；条件分支会按预算金额自动判定。'
              : '若该流程含发起人自选节点请在此选人，否则可直接提交（不确定时可留空）。'
          }}
        </p>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="showChoice = false">取消</NButton>
          <NButton type="primary" :loading="submitting" @click="doSubmit">确认提交</NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 单据预览 -->
    <NModal
      v-model:show="showPreview"
      preset="card"
      title="采购申请单预览"
      :style="{ width: '720px' }"
      :mask-closable="false"
    >
      <BizDocPreview table="biz_purchase_request" :id="previewId" />
      <template #footer>
        <NSpace justify="end">
          <NButton @click="showPreview = false">关闭</NButton>
          <NButton v-if="previewRow?.instanceId" type="primary" tertiary @click="openProgress(String(previewRow?.instanceId))">
            查看进度
          </NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 审批进度 -->
    <NDrawer v-model:show="showProgress" :width="520">
      <NDrawerContent title="审批进度" closable>
        <NSpin :show="progressLoading">
          <FlowTimeline :detail="progressDetail" />
        </NSpin>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<style scoped>
.biz-purchase__bar {
  margin-bottom: 12px;
}

.biz-purchase__hint {
  margin: 0 0 0 96px;
  font-size: 12px;
  line-height: 1.7;
}
</style>
