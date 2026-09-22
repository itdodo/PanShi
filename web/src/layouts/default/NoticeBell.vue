<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { NBadge, NButton, NEmpty, NList, NListItem, NPopover, NScrollbar, NSpin, NTabPane, NTabs, NTag, NTooltip } from 'naive-ui'
import { markMessageRead } from '@/api/notice'
import { useNoticeStore } from '@/stores/notice'
import { fromNow } from '@/utils/format'

/**
 * 顶栏铃铛：公告 + 站内信双 Tab（后端批次 #6 未上线时列表为空、角标 0，接口全部静默失败）。
 * 角标 = 待办数(/flow/task/todo-count) + 未读站内信；SignalR notice 事件由 useRealtime 触发 refreshAll。
 */
const notice = useNoticeStore()
const show = ref(false)
const tab = ref<'notice' | 'message'>('notice')
let timer = 0

const totalBadge = computed(() => notice.badge)
const messageItems = computed(() => notice.messages)
const noticeItems = computed(() => notice.notices)

async function readItem(id: string): Promise<void> {
  await markMessageRead(id).catch(() => undefined)
  await notice.refreshAll()
}

onMounted(() => {
  void notice.refreshAll()
  // SignalR 不可用时的兜底轮询（60s）
  timer = window.setInterval(() => void notice.refreshCount(), 60000)
})

onBeforeUnmount(() => {
  if (timer) window.clearInterval(timer)
})
</script>

<template>
  <NPopover
    v-model:show="show"
    trigger="click"
    placement="bottom"
    :width="360"
    :content-style="{ padding: '0' }"
  >
    <template #trigger>
      <NTooltip trigger="hover" :delay="400">
        <template #trigger>
          <NButton quaternary circle size="small" aria-label="通知中心">
            <NBadge :value="totalBadge" :max="99" :show="totalBadge > 0" :offset="[-2, 2]">
              <icon-lucide-bell />
            </NBadge>
          </NButton>
        </template>
        通知 / 待办
      </NTooltip>
    </template>

    <div class="ps-bell">
      <div class="ps-bell__head">
        <span class="ps-bell__title">消息中心</span>
        <NButton text size="tiny" @click="notice.refreshAll()">
          <template #icon><icon-lucide-refresh-ccw /></template>
          刷新
        </NButton>
      </div>
      <div class="ps-bell__stats">
        <NTag size="small" :bordered="false" type="info">待办 {{ notice.todoCount }}</NTag>
        <NTag size="small" :bordered="false" type="warning">未读 {{ notice.unreadCount }}</NTag>
        <NTag size="small" :bordered="false">公告 {{ noticeItems.length }}</NTag>
      </div>
      <NSpin :show="notice.loading">
        <NTabs v-model:value="tab" type="line" size="small" animated :style="{ padding: '0 12px' }">
          <NTabPane name="notice" :tab="`公告 (${noticeItems.length})`">
            <NScrollbar style="max-height: 300px">
              <NEmpty v-if="!noticeItems.length" description="暂无公告" size="small" />
              <NList v-else hoverable clickable :style="{ padding: 0 }">
                <NListItem v-for="item in noticeItems" :key="item.id">
                  <div class="ps-bell__item">
                    <div class="ps-ellipsis">{{ item.title }}</div>
                    <div class="ps-bell__time">{{ fromNow(item.publishTime ?? item.createTime) }}</div>
                  </div>
                </NListItem>
              </NList>
            </NScrollbar>
          </NTabPane>
          <NTabPane name="message" :tab="`站内信 (${messageItems.length})`">
            <NScrollbar style="max-height: 300px">
              <NEmpty v-if="!messageItems.length" description="暂无站内信" size="small" />
              <NList v-else hoverable clickable :style="{ padding: 0 }">
                <NListItem v-for="item in messageItems" :key="item.id" @click="readItem(item.id)">
                  <div class="ps-bell__item">
                    <div class="ps-bell__line">
                      <span v-if="!item.isRead" class="ps-bell__dot" />
                      <span class="ps-ellipsis">{{ item.title }}</span>
                    </div>
                    <div class="ps-bell__time">{{ item.senderName ?? '系统' }} · {{ fromNow(item.createTime) }}</div>
                  </div>
                </NListItem>
              </NList>
            </NScrollbar>
          </NTabPane>
        </NTabs>
      </NSpin>
      <div class="ps-bell__foot">
        <span class="ps-muted">接口未就绪时此处为空（批次 #12 接消息中心页）</span>
      </div>
    </div>
  </NPopover>
</template>

<style scoped>
.ps-bell {
  display: flex;
  flex-direction: column;
}

.ps-bell__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 12px 4px;
}

.ps-bell__title {
  font-weight: 600;
}

.ps-bell__stats {
  display: flex;
  gap: 6px;
  padding: 0 12px 6px;
}

.ps-bell__item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.ps-bell__line {
  display: flex;
  align-items: center;
  gap: 6px;
}

.ps-bell__dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--ps-primary);
  flex: none;
}

.ps-bell__time {
  font-size: 12px;
  color: var(--ps-text-3);
}

.ps-bell__foot {
  padding: 8px 12px;
  font-size: 12px;
  border-top: 1px solid rgba(128, 128, 128, 0.16);
}

:deep(svg) {
  width: 16px;
  height: 16px;
  display: block;
}
</style>
