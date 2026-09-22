<script setup lang="ts">
import { computed } from 'vue'
import { NEmpty, NTimeline, NTimelineItem } from 'naive-ui'
import type { FlowInstanceDetail } from '@/api/flow'
import { formatDateTime } from '@/utils/format'
import { actionMeta } from './flowEnums'

/**
 * 审批流转时间线：NTimeline 渲染 instanceDetail 的 records。
 * action → 文案/颜色（提交灰、通过绿、拒绝红、驳回橙、转办蓝、加签紫、撤回灰、抄送青、自动灰、作废红）。
 */
const props = defineProps<{
  detail?: FlowInstanceDetail | null
  /** 空数据时的提示文案 */
  emptyText?: string
}>()

const records = computed(() => props.detail?.records ?? [])

function titleOf(nodeName?: string | null, action?: string | null): string {
  const meta = actionMeta(action)
  return nodeName ? `${meta.label} · ${nodeName}` : meta.label
}
</script>

<template>
  <div class="flow-timeline">
    <NEmpty v-if="!records.length" :description="emptyText || '暂无流转记录'" size="small" />
    <NTimeline v-else>
      <NTimelineItem
        v-for="item in records"
        :key="item.id"
        :type="actionMeta(item.action).type"
        :color="actionMeta(item.action).color"
        :title="titleOf(item.nodeName, item.action)"
        :time="formatDateTime(item.createTime)"
      >
        <div class="flow-timeline__operator">{{ item.operatorName || '系统' }}</div>
        <div v-if="item.comment" class="flow-timeline__comment">{{ item.comment }}</div>
      </NTimelineItem>
    </NTimeline>
  </div>
</template>

<style scoped>
.flow-timeline__operator {
  font-size: 13px;
  color: var(--ps-text-3);
}

.flow-timeline__comment {
  margin-top: 4px;
  padding: 6px 10px;
  border-radius: 6px;
  background: rgba(100, 116, 139, 0.1);
  font-size: 13px;
  line-height: 1.7;
  white-space: pre-wrap;
  word-break: break-all;
}
</style>
