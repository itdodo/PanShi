<script setup lang="ts">
import { h, onMounted, reactive, ref } from 'vue'
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
import {
  INSTANCE_STATUS,
  instanceDetail,
  pageFlowDefs,
  pageInstances,
  voidInstance,
  type FlowInstanceDetail,
  type FlowInstanceDto
} from '@/api/flow'
import { usePageList } from '@/composables/usePageList'
import { formatDateTime } from '@/utils/format'
import { message } from '@/utils/feedback'
import FlowTimeline from '@/components/FlowTimeline.vue'
import { bizTableLabel, instanceStatusMeta } from '@/components/flowEnums'
import { hasPerm } from '@/directives/permission'

/** 流程实例（管理视角）：全量实例查询 + 作废（workflow:instance:void）+ 轨迹抽屉 */
const STATUS_OPTIONS: SelectOption[] = [
  { label: '全部', value: 0 },
  { label: '审批中', value: INSTANCE_STATUS.Running },
  { label: '已通过', value: INSTANCE_STATUS.Approved },
  { label: '已拒绝', value: INSTANCE_STATUS.Rejected },
  { label: '已撤回', value: INSTANCE_STATUS.Withdrawn },
  { label: '已作废', value: INSTANCE_STATUS.Voided }
]

const flowOptions = ref<{ label: string; value: string }[]>([])

const list = usePageList<FlowInstanceDto, { keyword: string; flowCode: string; status: number | null }>({
  fetcher: pageInstances,
  defaultQuery: () => ({ keyword: '', flowCode: '', status: null })
})
const { queryParams, loading, data, pagination, search, reset, load } = list

/** NSelect 不支持 null 值：flowCode 用空串、status 用 0 表示「全部」 */
function pickFlowCode(raw: unknown): void {
  queryParams.flowCode = raw === null || raw === undefined ? '' : String(raw)
  void search()
}

function pickStatus(raw: unknown): void {
  const value = Number(raw ?? 0)
  queryParams.status = value > 0 ? value : null
  void search()
}

const drawer = ref(false)
const detailLoading = ref(false)
const detail = ref<FlowInstanceDetail | null>(null)
const voidReason = reactive<Record<string, string>>({})

async function openDetail(row: FlowInstanceDto): Promise<void> {
  drawer.value = true
  detail.value = null
  detailLoading.value = true
  try {
    detail.value = await instanceDetail(row.id)
  } catch {
    /* 拦截器已提示 */
  } finally {
    detailLoading.value = false
  }
}

async function doVoid(row: FlowInstanceDto): Promise<boolean> {
  try {
    await voidInstance(row.id, voidReason[row.id]?.trim() || undefined)
    message.success('已作废该实例')
    delete voidReason[row.id]
    await load()
    return true
  } catch {
    return false
  }
}

onMounted(async () => {
  if (!hasPerm('workflow:def:list')) return
  try {
    const page = await pageFlowDefs({ pageNum: 1, pageSize: 200, status: 1 })
    const seen = new Set<string>()
    const opts: { label: string; value: string }[] = []
    for (const def of page?.rows ?? []) {
      if (seen.has(def.flowCode)) continue
      seen.add(def.flowCode)
      opts.push({ label: `${def.flowName}（${def.flowCode}）`, value: def.flowCode })
    }
    flowOptions.value = opts
  } catch {
    /* 无定义读取权限时下拉留空，仍可关键字检索 */
  }
})

const columns: DataTableColumns<FlowInstanceDto> = [
  {
    title: '单据摘要',
    key: 'summary',
    minWidth: 220,
    ellipsis: { tooltip: true },
    render: (row) =>
      h(NButton, { text: true, type: 'primary', onClick: () => openDetail(row) }, { default: () => row.summary || `实例 ${row.id}` })
  },
  { title: '单据类型', key: 'businessTable', width: 118, render: (row) => bizTableLabel(row.businessTable) },
  { title: '流程名称', key: 'flowName', width: 160, ellipsis: { tooltip: true }, render: (row) => row.flowName || row.flowCode },
  { title: '发起人', key: 'submitterName', width: 110, ellipsis: { tooltip: true } },
  {
    title: '当前节点',
    key: 'currentNodeCode',
    width: 130,
    render: (row) => (row.status === INSTANCE_STATUS.Running ? row.currentNodeCode || '—' : '—')
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
  { title: '结束时间', key: 'finishedTime', width: 168, render: (row) => formatDateTime(row.finishedTime) },
  {
    title: '操作',
    key: 'actions',
    width: 220,
    fixed: 'right',
    render: (row) => {
      const btns = [h(NButton, { size: 'tiny', tertiary: true, onClick: () => openDetail(row) }, { default: () => '详情' })]
      if (row.status === INSTANCE_STATUS.Running && hasPerm('workflow:instance:void')) {
        btns.push(
          h(
            NPopconfirm,
            { showIcon: false, onPositiveClick: () => doVoid(row) },
            {
              trigger: () => h(NButton, { size: 'tiny', type: 'error', tertiary: true }, { default: () => '作废' }),
              default: () =>
                h('div', { style: 'width:240px' }, [
                  h('div', { style: 'margin-bottom:6px;font-size:13px' }, '作废原因（留空=管理员作废）'),
                  h(NInput, {
                    value: voidReason[row.id] ?? '',
                    'onUpdate:value': (v: string) => (voidReason[row.id] = v),
                    type: 'textarea',
                    rows: 2,
                    maxlength: 200,
                    placeholder: '选填'
                  })
                ])
            }
          )
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
      <NSpace justify="space-between" align="end" :wrap="false" class="flow-instance__bar">
        <NForm inline :model="queryParams" @submit.prevent="search">
          <NFormItem label="关键字" path="keyword">
            <NInput
              v-model:value="queryParams.keyword"
              placeholder="摘要 / 发起人"
              clearable
              style="width: 200px"
              @keyup.enter="search"
            />
          </NFormItem>
          <NFormItem label="流程" path="flowCode">
            <NSelect
              :value="queryParams.flowCode || null"
              :options="flowOptions"
              clearable
              filterable
              placeholder="全部流程"
              style="width: 210px"
              @update:value="pickFlowCode"
            />
          </NFormItem>
          <NFormItem label="状态" path="status">
            <NSelect
              :value="queryParams.status ?? 0"
              :options="STATUS_OPTIONS"
              style="width: 130px"
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
        :scroll-x="1400"
      />
    </NCard>

    <NDrawer v-model:show="drawer" :width="540">
      <NDrawerContent :title="`流转轨迹 · ${detail?.instance.summary || ''}`" closable>
        <NSpin :show="detailLoading">
          <FlowTimeline :detail="detail" />
        </NSpin>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<style scoped>
.flow-instance__bar {
  margin-bottom: 12px;
}
</style>
