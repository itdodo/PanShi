<script setup lang="ts">
import { h, ref } from 'vue'
import {
  NButton,
  NCard,
  NSpin,
  NDescriptions,
  NDescriptionsItem,
  NDataTable,
  NDrawer,
  NDrawerContent,
  NSpace,
  NTag,
  type DataTableColumns
} from 'naive-ui'
import { instanceDetail, pageDone, type FlowInstanceDetail, type FlowTaskDto } from '@/api/flow'
import { usePageList } from '@/composables/usePageList'
import { formatDateTime } from '@/utils/format'
import FlowTimeline from '@/components/FlowTimeline.vue'
import { bizTableLabel, instanceStatusMeta, nodeModeLabel, taskStatusMeta } from '@/components/flowEnums'

/** 我的已办：历史审批轨迹，点摘要看整条流转时间线 */
const list = usePageList<FlowTaskDto>({ fetcher: pageDone })
const { loading, data, pagination, load } = list

const drawer = ref(false)
const detailLoading = ref(false)
const detail = ref<FlowInstanceDetail | null>(null)
const current = ref<FlowTaskDto | null>(null)

async function openDetail(row: FlowTaskDto): Promise<void> {
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
}

const columns: DataTableColumns<FlowTaskDto> = [
  {
    title: '单据摘要',
    key: 'summary',
    minWidth: 220,
    ellipsis: { tooltip: true },
    render: (row) =>
      h(
        NButton,
        { text: true, type: 'primary', onClick: () => openDetail(row) },
        { default: () => row.summary || '查看实例' }
      )
  },
  { title: '单据类型', key: 'businessTable', width: 120, render: (row) => bizTableLabel(row.businessTable) },
  { title: '发起人', key: 'submitterName', width: 110, ellipsis: { tooltip: true } },
  { title: '审批节点', key: 'nodeName', width: 150, ellipsis: { tooltip: true }, render: (row) => row.nodeName || row.nodeCode },
  {
    title: '模式',
    key: 'nodeMode',
    width: 88,
    render: (row) => h(NTag, { size: 'small', bordered: false, type: 'info' }, { default: () => nodeModeLabel(row.nodeMode) })
  },
  {
    title: '处理结果',
    key: 'status',
    width: 104,
    render: (row) => {
      const meta = taskStatusMeta(row.status)
      return h(NTag, { size: 'small', bordered: false, type: meta.type }, { default: () => meta.label })
    }
  },
  {
    title: '审批意见',
    key: 'comment',
    minWidth: 180,
    ellipsis: { tooltip: true },
    render: (row) => row.comment || h('span', { class: 'ps-muted' }, '—')
  },
  {
    title: '处理时间',
    key: 'handledTime',
    width: 168,
    render: (row) => formatDateTime(row.handledTime ?? row.createTime)
  }
]
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NSpace justify="space-between" align="center" class="flow-done__bar">
        <span class="ps-muted">共 {{ data.length }} 条（当前页）· 点击摘要查看完整流转轨迹</span>
        <NButton size="small" :loading="loading" @click="load">刷新</NButton>
      </NSpace>

      <NDataTable
        remote
        size="small"
        :columns="columns"
        :data="data"
        :loading="loading"
        :pagination="pagination"
        :row-key="(row: FlowTaskDto) => row.id"
        :scroll-x="1240"
      />
    </NCard>

    <NDrawer v-model:show="drawer" :width="520">
      <NDrawerContent :title="`流转轨迹 · ${current?.summary || current?.nodeName || '实例详情'}`" closable>
        <NSpin :show="detailLoading">
          <NDescriptions v-if="detail" :column="2" size="small" label-placement="left" class="flow-done__head">
            <NDescriptionsItem label="流程名称">{{ detail.instance.flowName }}</NDescriptionsItem>
            <NDescriptionsItem label="实例状态">
              <NTag size="small" :bordered="false" :type="instanceStatusMeta(detail.instance.status).type">
                {{ instanceStatusMeta(detail.instance.status).label }}
              </NTag>
            </NDescriptionsItem>
            <NDescriptionsItem label="发起人">{{ detail.instance.submitterName }}</NDescriptionsItem>
            <NDescriptionsItem label="提交时间">{{ formatDateTime(detail.instance.createTime) }}</NDescriptionsItem>
          </NDescriptions>
          <FlowTimeline :detail="detail" />
        </NSpin>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<style scoped>
.flow-done__bar {
  margin-bottom: 12px;
}

.flow-done__head {
  margin-bottom: 18px;
}
</style>
