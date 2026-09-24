<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import {
  NButton,
  NCard,
  NDrawer,
  NDrawerContent,
  NEmpty,
  NForm,
  NFormItem,
  NInput,
  NPopconfirm,
  NRadioButton,
  NRadioGroup,
  NSelect,
  NSpace,
  NSpin,
  NTag,
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
 * 消息中心（/sys/message）：收件箱为主体，「写消息」走右侧发送抽屉，点消息行就地展开阅读并标已读。
 * 收件箱接口为静默调用（api/notice 里 silent:true），后端未就绪时列表为空不弹错。
 */
const notice = useNoticeStore()

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
/** 就地展开阅读的消息 id（同时是标已读的触发） */
const selectedId = ref<string | null>(null)

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
  selectedId.value = null
  await loadInbox()
}

async function toggleOpen(item: MessageDto): Promise<void> {
  selectedId.value = selectedId.value === item.id ? null : item.id
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
    selectedId.value = null
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

/* ------------------------------- 发送抽屉 ------------------------------- */
const drawerVisible = ref(false)
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

function openSend(): void {
  drawerVisible.value = true
  if (!receiverOptions.value.length) void loadOptions()
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
    drawerVisible.value = false
    await Promise.allSettled([notice.refreshCount(), loadInbox(true)])
  } catch {
    /* 拦截器已提示 */
  } finally {
    sending.value = false
  }
}

onMounted(() => {
  void loadOptions()
  void loadInbox()
  void notice.refreshCount()
})
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <template #header>
        <div class="ps-message__head">
          <NSpace align="center" :size="8">
            <span class="ps-message__head-title">我的收件箱</span>
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
            <NButton v-permission="'sys:message:send'" size="small" type="primary" @click="openSend">
              <template #icon><icon-lucide-pen-square /></template>
              写消息
            </NButton>
          </NSpace>
        </div>
      </template>

      <NSpin :show="inboxLoading">
        <NEmpty v-if="!inboxLoading && !rows.length" description="收件箱暂无消息" style="padding: 48px 0" />
        <ul v-else class="ps-message__list">
          <li v-for="item in rows" :key="item.id" class="ps-message__item" :class="{ 'ps-message__item--open': selectedId === item.id }">
            <div class="ps-message__row" @click="toggleOpen(item)">
              <span class="ps-message__dot" :class="{ 'ps-message__dot--hidden': item.isRead }" />
              <span class="ps-message__title" :class="{ 'ps-message__title--unread': !item.isRead }">
                {{ item.title }}
              </span>
              <span class="ps-message__meta">{{ item.senderName || '系统' }} · {{ fromNow(item.createTime) }}</span>
              <NTag size="small" :bordered="false" :type="msgTypeTag(item.msgType).type">
                {{ msgTypeTag(item.msgType).label }}
              </NTag>
              <icon-lucide-chevron-down class="ps-message__caret" :class="{ 'ps-message__caret--open': selectedId === item.id }" />
            </div>
            <div v-if="selectedId === item.id" class="ps-message__detail">
              <p v-if="item.content" class="ps-message__detail-content">{{ item.content }}</p>
              <p v-else class="ps-message__detail-content ps-message__detail-content--empty">（无正文）</p>
              <div class="ps-message__detail-foot">
                <span>{{ formatDateTime(item.createTime) }}</span>
                <span v-if="item.readTime"> · 已读于 {{ formatDateTime(item.readTime) }}</span>
              </div>
            </div>
          </li>
        </ul>
      </NSpin>

      <NSpace justify="center" align="center" class="ps-message__more">
        <NButton v-if="!finished" size="small" tertiary :loading="loadingMore" @click="loadMore">
          加载更多（已显示 {{ rows.length }}/{{ total }}）
        </NButton>
        <span v-else-if="rows.length" class="ps-muted">已显示全部 {{ rows.length }} 条</span>
      </NSpace>
    </NCard>

    <NDrawer v-model:show="drawerVisible" :width="480" placement="right">
      <NDrawerContent title="写消息" closable>
        <NForm ref="sendFormRef" :model="sendModel" :rules="sendRules" label-placement="top">
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
              :autosize="{ minRows: 6, maxRows: 14 }"
              maxlength="2000"
              show-count
              placeholder="选填。接收方在顶栏铃铛与本页面收到，实时推送由 SignalR 负责。"
            />
          </NFormItem>
        </NForm>
        <template #footer>
          <NSpace justify="end" :size="8">
            <NButton tertiary @click="drawerVisible = false">取消</NButton>
            <NButton
              v-permission="'sys:message:send'"
              type="primary"
              :loading="sending"
              :disabled="optionsLoading && !receiverOptions.length"
              @click="doSend"
            >
              发送
            </NButton>
          </NSpace>
        </template>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<style scoped>
.ps-message__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}

.ps-message__head-title {
  font-size: 16px;
  font-weight: 600;
}

/* —— 列表：整行可点，展开阅读 —— */
.ps-message__list {
  list-style: none;
  margin: 4px 0 0;
  padding: 0;
}

.ps-message__item {
  border-radius: 10px;
  transition: background 0.16s ease;
}

.ps-message__item + .ps-message__item {
  margin-top: 2px;
}

.ps-message__item:hover {
  background: var(--ps-tab-hover);
}

.ps-message__item--open {
  background: var(--ps-tab-hover);
}

.ps-message__row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  cursor: pointer;
  min-width: 0;
}

.ps-message__dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--ps-primary);
  flex: none;
}

.ps-message__dot--hidden {
  background: transparent;
}

.ps-message__title {
  font-size: 14px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 0 1 auto;
}

.ps-message__title--unread {
  font-weight: 650;
}

.ps-message__meta {
  margin-left: auto;
  flex: none;
  font-size: 12px;
  color: var(--ps-text-3);
  white-space: nowrap;
}

.ps-message__caret {
  width: 14px;
  height: 14px;
  flex: none;
  color: var(--ps-text-3);
  transform: rotate(-90deg);
  transition: transform 0.18s ease;
}

.ps-message__caret--open {
  transform: rotate(0deg);
}

.ps-message__detail {
  padding: 0 12px 12px 29px;
}

.ps-message__detail-content {
  margin: 0;
  padding: 10px 12px;
  font-size: 13px;
  line-height: 1.7;
  white-space: pre-wrap;
  word-break: break-word;
  border: 1px solid var(--ps-card-border);
  border-radius: 8px;
  background: var(--ps-page-bg);
}

.ps-message__detail-content--empty {
  color: var(--ps-text-3);
}

.ps-message__detail-foot {
  margin-top: 6px;
  font-size: 12px;
  color: var(--ps-text-3);
}

.ps-message__more {
  margin-top: 12px;
  min-height: 24px;
}
</style>
