<script setup lang="ts">
import { h } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
  NForm,
  NFormItem,
  NInput,
  NSpace,
  NTag,
  type DataTableColumns
} from 'naive-ui'
import { pageTodo, type FlowTaskDto } from '@/api/flow'
import { usePageList } from '@/composables/usePageList'
import { useNoticeStore } from '@/stores/notice'
import { formatDateTime } from '@/utils/format'
import ApprovalActions from '@/components/ApprovalActions.vue'
import { bizTableLabel, nodeModeLabel } from '@/components/flowEnums'

/** 我的待办：行内直接完成审批动作（同意/拒绝/转办/加签/驳回） */
const notice = useNoticeStore()
const list = usePageList<FlowTaskDto, { keyword: string }>({
  fetcher: pageTodo,
  defaultQuery: () => ({ keyword: '' })
})
const { queryParams, loading, data, pagination, search, reset, load } = list

async function reload(): Promise<void> {
  await load()
  void notice.refreshCount()
}

const columns: DataTableColumns<FlowTaskDto> = [
  {
    title: '单据摘要',
    key: 'summary',
    minWidth: 220,
    ellipsis: { tooltip: true },
    render: (row) => row.summary || h('span', { class: 'ps-muted' }, '（无摘要）')
  },
  { title: '单据类型', key: 'businessTable', width: 120, render: (row) => bizTableLabel(row.businessTable) },
  { title: '发起人', key: 'submitterName', width: 110, ellipsis: { tooltip: true } },
  {
    title: '当前节点',
    key: 'nodeName',
    width: 150,
    ellipsis: { tooltip: true },
    render: (row) => row.nodeName || row.nodeCode
  },
  {
    title: '模式',
    key: 'nodeMode',
    width: 92,
    render: (row) =>
      h(NTag, { size: 'small', bordered: false, type: 'info' }, { default: () => nodeModeLabel(row.nodeMode) })
  },
  { title: '到达时间', key: 'createTime', width: 168, render: (row) => formatDateTime(row.createTime) },
  {
    title: '审批操作',
    key: 'actions',
    width: 286,
    fixed: 'right',
    render: (row) => h(ApprovalActions, { task: row, onDone: () => void reload() })
  }
]
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NSpace justify="space-between" align="end" :wrap="false" class="flow-todo__bar">
        <NForm inline :model="queryParams" @submit.prevent="search">
          <NFormItem label="关键字" path="keyword">
            <NInput
              v-model:value="queryParams.keyword"
              placeholder="按单据摘要搜索"
              clearable
              style="width: 240px"
              @keyup.enter="search"
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
        :row-key="(row: FlowTaskDto) => row.id"
        :scroll-x="1150"
        :row-props="() => ({ style: 'vertical-align: middle' })"
      >
        <template #empty>太清爽了——没有待办审批</template>
      </NDataTable>
    </NCard>
  </div>
</template>

<style scoped>
.flow-todo__bar {
  margin-bottom: 12px;
}
</style>
