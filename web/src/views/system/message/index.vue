<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import {
  NButton,
  NCard,
  NEmpty,
  NForm,
  NFormItem,
  NInput,
  NList,
  NListItem,
  NPopconfirm,
  NRadioButton,
  NRadioGroup,
  NSpace,
  NSpin,
  NTag,
  NTooltip,
  type FormInst,
  type FormRules,
  type SelectOption
} from 'naive-ui'
import { sendMessage, userOptions } from '@/api/admin'
import { markAllRead, markMessageRead, pageMyMessages, type MessageDto } from '@/api/notice'
import { useNoticeStore } from '@/stores/notice'
import { message } from '@/utils/feedback'
import { formatDateTime, fromNow } from '@/utils/format'

/**
 * 消息中心（/sys/message）：上「发送站内信」（sys:message:send），下「我的收件箱」（登录即可）。
 * 收件箱接口为静默调用（api/notice 里 silent:true），后端未就绪时列表为空不弹错。
 */
const notice = useNoticeStore()

/* ------------------------------- 发送表单 ------------------------------- */
const sendFormRef = ref<FormInst | null>(null)
const sending = ref(false)
const optionsLoading = ref(false)
const receiverOptions = ref<SelectOption[]>([])

const sendModel = reactive<{ receiverIds: number[]; title: string; content: string }>({
  receiverIds: [],
  title: '',
  content: ''
})

const sendRules: FormRules = {
  receiverIds: [{ type: 'array', required: true, message: '请至少选择一位接收人', trigger: ['change', 'blur'] }],
  title: [
    { required: true, message: '请输入标题', trigger: ['input', 'blur'] },
    { max: 128, message: '标题不超过 128 字', trigger: ['input', 'blur'] }
  ]
}

async function loadOptions(): Promise<void> {
  optionsLoading.value = true
  try {
    const list = await userOptions()
    // 契约：MessageSendDto.ReceiverIds 是 List<long>，故值必须转 Number
    receiverOptions.value = (list ?? []).map((it) => ({
      label: it.label || it.value,
      value: Number(it.value)
    }))
  } catch {
    receiverOptions.value = []
  } finally {
    optionsLoading.value = false
  }
}

function resetSendForm(): void {
  sendModel.receiverIds = []
  sendModel.title = ''
  sendModel.content = ''
  sendFormRef.value?.restoreValidation()
}

async function doSend(): Promise<void> {
  const invalid = await sendFormRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return
  sending.value = true
  try {
    await sendMessage({
      receiverIds: sendModel.receiverIds.map((v) => Number(v)),
      title: sendModel.title.trim(),
      content: sendModel.content.trim() || undefined
    })
    message.success(`已发送给 ${sendModel.receiverIds.length} 位接收人`)
    resetSendForm()
    await Promise.allSettled([notice.refreshCount(), loadInbox(true)])
  } catch {
    /* 拦截器已提示 */
  } finally {
    sending.value = false
  }
}

/* ------------------------------- 我的收件箱 ------------------------------- */
type ReadFilter = 'all' | 'unread' | 'read'

const PAGE_SIZE = 10

const filter = ref<ReadFilter>('all')
const rows = ref<MessageDto[]>([])
const total = ref(0)
const pageNum = ref(1)
const inboxLoading = ref(false)
const loadingMore = ref(false)
const marking = ref(false)

const isReadParam = (f: ReadFilter): boolean | undefined => (f === 'all' ? undefined : f === 'read')

const finished = computed(() => rows.value.length >= total.value)

async function loadInbox(silent = false): Promise<void> {
  if (!silent) inboxLoading.value = true
  pageNum.value = 1
  try {
    const result = await pageMyMessages({
      pageNum: 1,
      pageSize: PAGE_SIZE,
      isRead: isReadParam(filter.value)
    })
    rows.value = result?.rows ?? []
    total.value = Number(result?.total ?? 0)
  } catch {
    rows.value = []
    total.value = 0
  } finally {
    inboxLoading.value = false
  }
}

async function loadMore(): Promise<void> {
  if (finished.value) return
  loadingMore.value = true
  try {
    const next = pageNum.value + 1
    const result = await pageMyMessages({
      pageNum: next,
      pageSize: PAGE_SIZE,
      isRead: isReadParam(filter.value)
    })
    rows.value = [...rows.value, ...(result?.rows ?? [])]
    total.value = Number(result?.total ?? total.value)
    pageNum.value = next
  } catch {
    /* 静默接口：保留已加载数据 */
  } finally {
    loadingMore.value = false
  }
}

async function onFilterChange(value: string | number | boolean): Promise<void> {
  filter.value = value as ReadFilter
  await loadInbox()
}

async function openMessage(item: MessageDto): Promise<void> {
  if (item.isRead) return
  try {
    await markMessageRead(item.id)
    item.isRead = true
    item.readTime = new Date().toISOString()
    await notice.refreshCount()
  } catch {
    /* 静默接口，失败不打扰 */
  }
}

async function onReadAll(): Promise<void> {
  marking.value = true
  try {
    await markAllRead()
    message.success('已全部标记为已读')
    await Promise.allSettled([loadInbox(true), notice.refreshCount()])
  } catch {
    /* 拦截器/静默处理 */
  } finally {
    marking.value = false
  }
}

const MSG_TYPE_MAP: Record<number, { label: string; type: 'info' | 'success' | 'warning' }> = {
  1: { label: '系统', type: 'info' },
  2: { label: '站内信', type: 'success' },
  3: { label: '业务', type: 'warning' }
}

function msgTypeTag(msgType: number | string | undefined): { label: string; type: 'info' | 'success' | 'warning' } {
  const key = typeof msgType === 'string' ? Number(msgType) : msgType
  return MSG_TYPE_MAP[Number(key)] ?? { label: '消息', type: 'info' }
}

onMounted(() => {
  void loadOptions()
  void loadInbox()
  void notice.refreshCount()
})
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false" title="发送站内信" class="ps-message__card">
      <NForm
        ref="sendFormRef"
        :model="sendModel"
        :rules="sendRules"
        label-placement="left"
        label-width="88"
        class="ps-message__form"
      >
        <NFormItem label="接收人" path="receiverIds">
          <NSelect
            v-model:value="sendModel.receiverIds"
            multiple
            filterable
            clearable
            :options="receiverOptions"
            :loading="optionsLoading"
            max-tag-count="responsive"
            placeholder="搜索并选择接收人（可多选）"
          />
        </NFormItem>
        <NFormItem label="标题" path="title">
          <NInput v-model:value="sendModel.title" maxlength="128" show-count clearable placeholder="一句话说明来意" />
        </NFormItem>
        <NFormItem label="内容" path="content">
          <NInput
            v-model:value="sendModel.content"
            type="textarea"
            :autosize="{ minRows: 4, maxRows: 10 }"
            maxlength="2000"
            show-count
            placeholder="选填。接收方在顶栏铃铛与本页面收到，实时推送由 SignalR 负责。"
          />
        </NFormItem>
        <NFormItem :show-label="false">
          <NSpace :size="8">
            <NButton
              v-permission="'sys:message:send'"
              type="primary"
              :loading="sending"
              :disabled="optionsLoading && !receiverOptions.length"
              @click="doSend"
            >
              发送
            </NButton>
            <NButton tertiary @click="resetSendForm">清空</NButton>
            <NButton tertiary :loading="optionsLoading" @click="loadOptions">重载接收人</NButton>
          </NSpace>
        </NFormItem>
      </NForm>
    </NCard>

    <NCard :bordered="false">
      <template #header>
        <NSpace align="center" justify="space-between" :wrap="true">
          <NSpace align="center" :size="8">
            <span>我的收件箱</span>
            <NTag size="small" :bordered="false" type="warning">未读 {{ notice.unreadCount }}</NTag>
            <NTag size="small" :bordered="false">共 {{ total }} 条</NTag>
          </NSpace>
          <NSpace align="center" :size="8" :wrap="false">
            <NRadioGroup :value="filter" size="small" @update:value="onFilterChange">
              <NRadioButton value="all">全部</NRadioButton>
              <NRadioButton value="unread">未读</NRadioButton>
              <NRadioButton value="read">已读</NRadioButton>
            </NRadioGroup>
            <NButton size="small" tertiary :loading="inboxLoading" @click="loadInbox()">刷新</NButton>
            <NPopconfirm @positive-click="onReadAll">
              <template #trigger>
                <NButton size="small" type="primary" tertiary :loading="marking" :disabled="notice.unreadCount === 0">
                  全部已读
                </NButton>
              </template>
              确认把全部未读消息标记为已读？
            </NPopconfirm>
          </NSpace>
        </NSpace>
      </template>

      <NSpin :show="inboxLoading">
        <NEmpty v-if="!inboxLoading && !rows.length" description="收件箱暂无消息" style="padding: 32px 0" />
        <NList v-else hoverable clickable :bordered="false">
          <NListItem v-for="item in rows" :key="item.id" @click="openMessage(item)">
            <div class="ps-message__item">
              <div class="ps-message__line">
                <span v-if="!item.isRead" class="ps-message__dot" />
                <span class="ps-message__title" :class="{ 'ps-message__title--unread': !item.isRead }">
                  {{ item.title }}
                </span>
                <NTag size="small" :bordered="false" :type="msgTypeTag(item.msgType).type">
                  {{ msgTypeTag(item.msgType).label }}
                </NTag>
                <NTag v-if="item.isRead" size="small" :bordered="false">已读</NTag>
              </div>
              <p v-if="item.content" class="ps-message__content">{{ item.content }}</p>
              <div class="ps-message__time">
                {{ item.senderName || '系统' }} · {{ fromNow(item.createTime) }}
                <NTooltip trigger="hover">
                  <template #trigger>
                    <span class="ps-message__time-more">（{{ formatDateTime(item.createTime) }}）</span>
                  </template>
                  接收人本人可见；点击条目即标记已读
                </NTooltip>
              </div>
            </div>
          </NListItem>
        </NList>
      </NSpin>

      <NSpace justify="center" class="ps-message__more">
        <NButton v-if="!finished" size="small" tertiary :loading="loadingMore" @click="loadMore">
          加载更多（已显示 {{ rows.length }}/{{ total }}）
        </NButton>
        <span v-else-if="rows.length" class="ps-muted">已显示全部 {{ rows.length }} 条</span>
      </NSpace>
    </NCard>
  </div>
</template>

<style scoped>
.ps-message__card {
  margin-bottom: 14px;
}

.ps-message__form {
  max-width: 720px;
}

.ps-message__item {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.ps-message__line {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.ps-message__dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--ps-primary);
  flex: none;
}

.ps-message__title {
  font-size: 14px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ps-message__title--unread {
  font-weight: 650;
}

.ps-message__content {
  margin: 0;
  font-size: 13px;
  color: var(--ps-text-3);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.ps-message__time {
  font-size: 12px;
  color: var(--ps-text-3);
}

.ps-message__time-more {
  cursor: default;
}

.ps-message__more {
  margin-top: 12px;
}
</style>
