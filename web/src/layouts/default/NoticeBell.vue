<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import DOMPurify from 'dompurify'
import { NButton, NBadge, NEmpty, NList, NListItem, NPopover, NScrollbar, NSpin, NTabPane, NTabs, NTag, NTooltip } from 'naive-ui'
import { markMessageRead } from '@/api/notice'
import { useNoticeStore } from '@/stores/notice'
import { hasPerm } from '@/directives/permission'
import { formatDateTime, fromNow } from '@/utils/format'

/**
 * 顶栏铃铛：公告 + 站内信双 Tab，条目就地看详情（不跳页，所以普通员工也能读）。
 * 角标 = 待办数(/flow/task/todo-count) + 未读站内信；SignalR notice 事件由 useRealtime 触发 refreshAll。
 * 数据源都是「登录即可」的接口；只有页脚的「消息中心」入口按 sys:message:list 权限显隐。
 */
const notice = useNoticeStore()
const router = useRouter()
const show = ref(false)
const tab = ref<'notice' | 'message'>('notice')
let timer = 0

const totalBadge = computed(() => notice.badge)
const messageItems = computed(() => notice.messages)
const noticeItems = computed(() => notice.notices)
const canOpenCenter = computed(() => hasPerm('sys:message:list'))

/** 就地详情：公告正文是 wangEditor 的 HTML，必须 DOMPurify 净化后再 v-html */
const detail = ref<{
  kind: 'notice' | 'message'
  title: string
  meta: string
  html: string
  rich: boolean
} | null>(null)

function openNotice(id: string): void {
  const item = noticeItems.value.find((n) => n.id === id)
  if (!item) return
  detail.value = {
    kind: 'notice',
    title: item.title,
    meta: `发布于 ${formatDateTime(item.publishTime ?? item.createTime)}`,
    html: DOMPurify.sanitize(item.content ?? ''),
    rich: true
  }
}

async function openMessage(id: string): Promise<void> {
  const item = messageItems.value.find((m) => m.id === id)
  if (!item) return
  detail.value = {
    kind: 'message',
    title: item.title,
    meta: `${item.senderName ?? '系统'} · ${formatDateTime(item.createTime)}`,
    html: item.content ?? '',
    rich: false
  }
  if (!item.isRead) {
    await markMessageRead(id).catch(() => undefined)
    await notice.refreshAll()
  }
}

function goCenter(): void {
  show.value = false
  void router.push('/system/message')
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
        <span class="ps-bell__title">{{ detail ? '详情' : '消息中心' }}</span>
        <NButton v-if="!detail" text size="tiny" @click="notice.refreshAll()">
          <template #icon><icon-lucide-refresh-ccw /></template>
          刷新
        </NButton>
        <NButton v-else text size="tiny" @click="detail = null">
          <template #icon><icon-lucide-arrow-left /></template>
          返回
        </NButton>
      </div>
      <div v-if="!detail" class="ps-bell__stats">
        <NTag size="small" :bordered="false" type="info">待办 {{ notice.todoCount }}</NTag>
        <NTag size="small" :bordered="false" type="warning">未读 {{ notice.unreadCount }}</NTag>
        <NTag size="small" :bordered="false">公告 {{ noticeItems.length }}</NTag>
      </div>
      <div v-else class="ps-bell__stats">
        <NTag size="small" :bordered="false" :type="detail.kind === 'notice' ? 'info' : 'success'">
          {{ detail.kind === 'notice' ? '公告' : '站内信' }}
        </NTag>
        <span class="ps-muted ps-bell__detail-meta">{{ detail.meta }}</span>
      </div>
      <NSpin :show="notice.loading">
        <div v-if="detail" class="ps-bell__detail">
          <div class="ps-bell__detail-title">{{ detail.title }}</div>
          <!-- 公告正文是富文本，已在脚本里过 DOMPurify；站内信按纯文本渲染，走插值自动转义 -->
          <div v-if="detail.rich && detail.html" class="ps-rich-content" v-html="detail.html"></div>
          <div v-else-if="detail.rich" class="ps-muted">（无正文）</div>
          <div v-else-if="detail.html" class="ps-bell__detail-text">{{ detail.html }}</div>
          <div v-else class="ps-muted">（无正文）</div>
        </div>
        <NTabs v-else v-model:value="tab" type="line" size="small" animated :style="{ padding: '0 12px' }">
          <NTabPane name="notice" :tab="`公告 (${noticeItems.length})`">
            <NScrollbar style="max-height: 300px">
              <NEmpty v-if="!noticeItems.length" description="暂无公告" size="small" />
              <NList v-else hoverable clickable :style="{ padding: 0 }">
                <NListItem v-for="item in noticeItems" :key="item.id" @click="openNotice(item.id)">
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
                <NListItem v-for="item in messageItems" :key="item.id" @click="openMessage(item.id)">
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
        <span class="ps-muted">{{ detail ? '读完可点左上角返回' : `公告与站内信各显示最近 ${noticeItems.length} / ${messageItems.length} 条` }}</span>
        <NButton v-if="!detail && canOpenCenter" text size="tiny" type="primary" @click="goCenter">消息中心</NButton>
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

.ps-bell__detail-meta {
  font-size: 12px;
  align-self: center;
}

.ps-bell__detail {
  max-height: 320px;
  overflow-y: auto;
  padding: 4px 14px 12px;
}

.ps-bell__detail-title {
  font-size: 14px;
  font-weight: 600;
  margin-bottom: 8px;
}

.ps-bell__detail-text {
  font-size: 13px;
  line-height: 1.7;
  white-space: pre-wrap;
  word-break: break-word;
}

/* 公告正文是 v-html 注入的，scoped 样式够不到，必须 :deep（样式不外泄，与公告详情页同一做法） */
.ps-bell__detail :deep(p) {
  margin: 0 0 8px;
}

.ps-bell__detail :deep(ul),
.ps-bell__detail :deep(ol) {
  margin: 0 0 8px 18px;
}

.ps-bell__detail :deep(img) {
  max-width: 100%;
  border-radius: 8px;
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
