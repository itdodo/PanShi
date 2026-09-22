<script setup lang="ts">
import { h, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
  NDescriptions,
  NDescriptionsItem,
  NDrawer,
  NDrawerContent,
  NSpace,
  NSpin,
  NTag,
  type DataTableColumns
} from 'naive-ui'
import { instanceDetail, markCcRead, pageCcMe, type FlowCcDto, type FlowInstanceDetail } from '@/api/flow'
import { usePageList } from '@/composables/usePageList'
import { useNoticeStore } from '@/stores/notice'
import { formatDateTime } from '@/utils/format'
import FlowTimeline from '@/components/FlowTimeline.vue'
import { bizTableLabel, instanceStatusMeta } from '@/components/flowEnums'

/** 抄送我的：阅知型列表，打开详情即标记已读 */
const notice = useNoticeStore()
const list = usePageList<FlowCcDto>({ fetcher: pageCcMe, pageSize: 20 })
const { loading, data, pagination, load } = list

const drawer = ref(false)
const detailLoading = ref(false)
const detail = ref<FlowInstanceDetail | null>(null)
const current = ref<FlowCcDto | null>(null)

async function openRow(row: FlowCcDto): Promise<void> {
  current.value = row
  detail.value = null
  drawer.value = true
  detailLoading.value = true
  try {
    detail.value = await instanceDetail(row.instanceId)
  } catch {
    /* 拦截器已提示 */
  } finally {
    detailLoading.value = false
  }
  if (!row.isRead) {
    try {
      await markCcRead(row.id)
      row.isRead = true
      void notice.refreshCount()
    } catch {
      /* 拦截器已提示 */
    }
  }
}

const unreadCount = (): number => data.value.filter((row) => !row.isRead).length

const columns: DataTableColumns<FlowCcDto> = [
  {
    title: '实例摘要',
    key: 'summary',
    minWidth: 240,
    ellipsis: { tooltip: true },
    render: (row) =>
      h(
        NButton,
        { text: true, type: 'primary', onClick: () => openRow(row) },
        { default: () => row.instance?.summary || `实例 ${row.instanceId}` }
      )
  },
  {
    title: '流程名称',
    key: 'flowName',
    width: 170,
    ellipsis: { tooltip: true },
    render: (row) => row.instance?.flowName || row.instance?.flowCode || '-'
  },
  {
    title: '发起人',
    key: 'submitterName',
    width: 110,
    render: (row) => row.instance?.submitterName || '-'
  },
  {
    title: '单据类型',
    key: 'businessTable',
    width: 118,
    render: (row) => bizTableLabel(row.instance?.businessTable)
  },
  {
    title: '抄送节点',
    key: 'nodeCode',
    width: 140,
    ellipsis: { tooltip: true },
    render: (row) => row.nodeCode || '—'
  },
  {
    title: '实例状态',
    key: 'instanceStatus',
    width: 100,
    render: (row) => {
      if (!row.instance) return '—'
      const meta = instanceStatusMeta(row.instance.status)
      return h(NTag, { size: 'small', bordered: false, type: meta.type }, { default: () => meta.label })
    }
  },
  {
    title: '抄送时间',
    key: 'createTime',
    width: 168,
    render: (row) => formatDateTime(row.createTime)
  },
  {
    title: '已读',
    key: 'isRead',
    width: 84,
    render: (row) =>
      h(NTag, { size: 'small', bordered: false, type: row.isRead ? 'default' : 'warning' }, { default: () => (row.isRead ? '已读' : '未读') })
  }
]

function rowProps(row: FlowCcDto) {
  return { style: 'cursor: pointer', onClick: () => openRow(row) }
}
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NSpace justify="space-between" align="center" class="flow-cc__bar">
        <span class="ps-muted">本页未读 {{ unreadCount() }} 条 · 点击任意行查看进度并自动标记已读</span>
        <NButton size="small" :loading="loading" @click="load">刷新</NButton>
      </NSpace>

      <NDataTable
        remote
        size="small"
        :columns="columns"
        :data="data"
        :loading="loading"
        :pagination="pagination"
        :row-key="(row: FlowCcDto) => row.id"
        :row-props="rowProps"
        :scroll-x="1200"
      />
    </NCard>

    <NDrawer v-model:show="drawer" :width="540">
      <NDrawerContent :title="`抄送详情 · ${current?.instance?.summary || ''}`" closable>
        <NSpin :show="detailLoading">
          <NDescriptions v-if="detail" :column="2" size="small" label-placement="left" class="flow-cc__head">
            <NDescriptionsItem label="流程名称">{{ detail.instance.flowName }}</NDescriptionsItem>
            <NDescriptionsItem label="发起人">{{ detail.instance.submitterName }}</NDescriptionsItem>
            <NDescriptionsItem label="提交时间">{{ formatDateTime(detail.instance.createTime) }}</NDescriptionsItem>
            <NDescriptionsItem label="实例状态">
              <NTag size="small" :bordered="false" :type="instanceStatusMeta(detail.instance.status).type">
                {{ instanceStatusMeta(detail.instance.status).label }}
              </NTag>
            </NDescriptionsItem>
          </NDescriptions>
          <FlowTimeline :detail="detail" />
        </NSpin>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<style scoped>
.flow-cc__bar {
  margin-bottom: 12px;
}

.flow-cc__head {
  margin-bottom: 18px;
}
</style>
