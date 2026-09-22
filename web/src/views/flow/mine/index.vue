<script setup lang="ts">
import { h, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  NButton,
  NCard,
  NDataTable,
  NDrawer,
  NDrawerContent,
  NForm,
  NFormItem,
  NInput,
  NPopconfirm,
  NSelect,
  NSpace,
  NSpin,
  NTag,
  type DataTableColumns,
  type SelectOption
} from 'naive-ui'
import { INSTANCE_STATUS, instanceDetail, pageMyInstances, withdrawInstance, type FlowInstanceDetail, type FlowInstanceDto } from '@/api/flow'
import { usePageList } from '@/composables/usePageList'
import { useNoticeStore } from '@/stores/notice'
import { formatDateTime } from '@/utils/format'
import { message } from '@/utils/feedback'
import FlowTimeline from '@/components/FlowTimeline.vue'
import { bizTableLabel, bizTableRoute, instanceStatusMeta } from '@/components/flowEnums'

/** 我发起的：进度查看 + 撤回（仅审批中且无人处理）+ 被拒/撤回后回到业务页改单重提 */
const router = useRouter()
const notice = useNoticeStore()

const STATUS_OPTIONS: SelectOption[] = [
  { label: '全部', value: 0 },
  { label: '审批中', value: INSTANCE_STATUS.Running },
  { label: '已通过', value: INSTANCE_STATUS.Approved },
  { label: '已拒绝', value: INSTANCE_STATUS.Rejected },
  { label: '已撤回', value: INSTANCE_STATUS.Withdrawn },
  { label: '已作废', value: INSTANCE_STATUS.Voided }
]

/** NSelect 不支持 null 值：用 0 表示「全部」，落查询时转 null */
function pickStatus(raw: unknown): void {
  const value = Number(raw ?? 0)
  queryParams.status = value > 0 ? value : null
  void search()
}

const list = usePageList<FlowInstanceDto, { keyword: string; status: number | null }>({
  fetcher: pageMyInstances,
  defaultQuery: () => ({ keyword: '', status: null })
})
const { queryParams, loading, data, pagination, search, reset, load } = list

const drawer = ref(false)
const detailLoading = ref(false)
const detail = ref<FlowInstanceDetail | null>(null)
/** 节点编码 → 节点名（打开过详情的实例回填，列表首屏按编码显示） */
const nodeNames = ref<Record<string, string>>({})

async function openDetail(row: FlowInstanceDto): Promise<void> {
  drawer.value = true
  detail.value = null
  detailLoading.value = true
  try {
    const result = await instanceDetail(row.id)
    detail.value = result
    const merged: Record<string, string> = { ...nodeNames.value }
    for (const task of result?.tasks ?? []) if (task.nodeCode) merged[task.nodeCode] = task.nodeName || task.nodeCode
    for (const record of result?.records ?? []) if (record.nodeCode) merged[record.nodeCode] = record.nodeName || record.nodeCode
    nodeNames.value = merged
  } catch {
    /* 拦截器已提示 */
  } finally {
    detailLoading.value = false
  }
}

async function doWithdraw(row: FlowInstanceDto): Promise<boolean> {
  try {
    await withdrawInstance(row.id)
    message.success('已撤回，单据回到可编辑状态')
    await Promise.all([load(), notice.refreshCount()])
    return true
  } catch {
    // 拦截器已弹错（如「已有人处理，不能撤回」），此处仅刷新列表回到真实状态
    await load()
    return false
  }
}

function resubmit(row: FlowInstanceDto): void {
  void router.push({
    path: bizTableRoute(row.businessTable),
    query: { id: row.businessId, docNo: row.summary ?? '', resubmit: '1' }
  })
}

function nodeName(code?: string | null): string {
  if (!code) return '—'
  return nodeNames.value[code] ?? code
}

const columns: DataTableColumns<FlowInstanceDto> = [
  {
    title: '单据摘要',
    key: 'summary',
    minWidth: 210,
    ellipsis: { tooltip: true },
    render: (row) =>
      h(NButton, { text: true, type: 'primary', onClick: () => openDetail(row) }, { default: () => row.summary || row.flowName })
  },
  { title: '单据类型', key: 'businessTable', width: 118, render: (row) => bizTableLabel(row.businessTable) },
  { title: '流程名称', key: 'flowName', width: 150, ellipsis: { tooltip: true }, render: (row) => row.flowName || row.flowCode },
  {
    title: '当前节点',
    key: 'currentNodeCode',
    width: 140,
    ellipsis: { tooltip: true },
    render: (row) => (row.status === INSTANCE_STATUS.Running ? nodeName(row.currentNodeCode) : '—')
  },
  {
    title: '状态',
    key: 'status',
    width: 96,
    render: (row) => {
      const meta = instanceStatusMeta(row.status)
      return h(NTag, { size: 'small', bordered: false, type: meta.type }, { default: () => meta.label })
    }
  },
  { title: '提交时间', key: 'createTime', width: 168, render: (row) => formatDateTime(row.createTime) },
  {
    title: '结束时间',
    key: 'finishedTime',
    width: 168,
    render: (row) => formatDateTime(row.finishedTime)
  },
  {
    title: '操作',
    key: 'actions',
    width: 210,
    fixed: 'right',
    render: (row) => {
      const btns = [
        h(NButton, { size: 'tiny', tertiary: true, onClick: () => openDetail(row) }, { default: () => '进度' })
      ]
      if (row.status === INSTANCE_STATUS.Running) {
        btns.push(
          h(
            NPopconfirm,
            { onPositiveClick: () => doWithdraw(row) },
            {
              trigger: () => h(NButton, { size: 'tiny', type: 'warning', tertiary: true }, { default: () => '撤回' }),
              default: () => '撤回后需重新提交，且要求尚无人处理，确认撤回？'
            }
          )
        )
      }
      if (row.status === INSTANCE_STATUS.Rejected || row.status === INSTANCE_STATUS.Withdrawn) {
        btns.push(
          h(NButton, { size: 'tiny', type: 'primary', tertiary: true, onClick: () => resubmit(row) }, { default: () => '再提交' })
        )
      }
      return h(NSpace, { size: 6, wrap: false }, { default: () => btns })
    }
  }
]
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NSpace justify="space-between" align="end" :wrap="false" class="flow-mine__bar">
        <NForm inline :model="queryParams" @submit.prevent="search">
          <NFormItem label="关键字" path="keyword">
            <NInput
              v-model:value="queryParams.keyword"
              placeholder="摘要 / 发起人"
              clearable
              style="width: 220px"
              @keyup.enter="search"
            />
          </NFormItem>
          <NFormItem label="状态" path="status">
            <NSelect
              :value="queryParams.status ?? 0"
              :options="STATUS_OPTIONS"
              style="width: 140px"
              @update:value="pickStatus"
            />
          </NFormItem>
        </NForm>
        <NSpace>
          <NButton @click="reset">重置</NButton>
          <NButton type="primary" :loading="loading" @click="search">查询</NButton>
        </NSpace>
      </NSpace>

      <NDataTable
        remote
        size="small"
        :columns="columns"
        :data="data"
        :loading="loading"
        :pagination="pagination"
        :row-key="(row: FlowInstanceDto) => row.id"
        :scroll-x="1250"
      />
    </NCard>

    <NDrawer v-model:show="drawer" :width="520">
      <NDrawerContent :title="`审批进度 · ${detail?.instance.summary || ''}`" closable>
        <NSpin :show="detailLoading">
          <FlowTimeline :detail="detail" />
        </NSpin>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<style scoped>
.flow-mine__bar {
  margin-bottom: 12px;
}
</style>
