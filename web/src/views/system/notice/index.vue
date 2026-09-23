<script setup lang="ts">
import { computed, h, reactive, ref } from 'vue'
import dayjs from 'dayjs'
import DOMPurify from 'dompurify'
import {
  NButton,
  NCard,
  NDataTable,
  NDatePicker,
  NForm,
  NFormItem,
  NInput,
  NModal,
  NPopconfirm,
  NRadio,
  NRadioGroup,
  NSelect,
  NSpace,
  NTag,
  type DataTableColumns,
  type FormInst,
  type FormRules
} from 'naive-ui'
import { del, get, post, put } from '@/api/http'
import type { PagedResult } from '@/api/types'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'
import RichTextEditor from '@/components/RichTextEditor.vue'
import { NOTICE_STATUS, NOTICE_STATUS_OPTIONS, NOTICE_TYPE_OPTIONS, noticeStatusTag, noticeTypeLabel, toNum } from '../_shared'

/**
 * 公告管理（/sys/notice）：分页 + CRUD + 三态状态（0 停用 / 1 已发布 / 2 定时发布）。
 * 后端 NoticeDto = { id, title, noticeType, content, status, publishTime, createByName, createTime, version }；
 * 新增 POST /sys/notice、编辑 PUT /sys/notice/{id}（必带 version）、删除 DELETE /sys/notice/{id}。
 * 定时档校验「发布时间必须晚于当前时间」（后端 NoticeService 同规则兜底），到期由 sys.notice.publish 作业置为已发布。
 * 正文用 wangEditor 富文本（红线 #10：弹层 after-enter 后才挂载编辑器）；详情渲染前经 DOMPurify 净化防 XSS。
 */
type NoticeRow = {
  id: string
  title: string
  noticeType: number
  content?: string | null
  status: number
  publishTime?: string | null
  createByName?: string | null
  createTime: string
  version: number
}

type NoticeQueryModel = { title: string; noticeType: number | null; status: number | null }

type NoticeFormModel = {
  id: string | null
  title: string
  noticeType: number
  content: string
  status: number
  /** 毫秒时间戳（NDatePicker），提交转 ISO */
  publishTime: number | null
  version: number
}

const list = usePageList<NoticeRow, NoticeQueryModel>({
  fetcher: (q) => get<PagedResult<NoticeRow>>('/sys/notice/page', q),
  defaultQuery: () => ({ title: '', noticeType: null, status: null })
})

const modalVisible = ref(false)
/** 红线 #10：富文本编辑器仅在弹层动画结束后挂载（可见容器内初始化） */
const modalReady = ref(false)
const saving = ref(false)
const editing = computed(() => !!form.id)
const isScheduled = computed(() => form.status === NOTICE_STATUS.Scheduled)
const formRef = ref<FormInst | null>(null)

const form = reactive<NoticeFormModel>({
  id: null,
  title: '',
  noticeType: 1,
  content: '',
  status: NOTICE_STATUS.Stopped,
  publishTime: null,
  version: 0
})

const rules = computed<FormRules>(() => ({
  title: [{ required: true, max: 256, message: '请输入公告标题', trigger: ['input', 'blur'] }],
  publishTime: [
    {
      validator: (_rule: unknown, value: number | null) => {
        if (!isScheduled.value) return true
        if (!value) return new Error('定时发布必须选择发布时间')
        if (value <= Date.now()) return new Error('定时发布时间必须晚于当前时间')
        return true
      },
      trigger: ['update:value', 'change']
    }
  ]
}))

function resetForm(): void {
  form.id = null
  form.title = ''
  form.noticeType = 1
  form.content = ''
  form.status = NOTICE_STATUS.Stopped
  form.publishTime = null
  form.version = 0
  modalReady.value = false
}

function openCreate(): void {
  resetForm()
  modalVisible.value = true
}

function openEdit(row: NoticeRow): void {
  form.id = row.id
  form.title = row.title
  form.noticeType = toNum(row.noticeType, 1)
  form.content = row.content ?? ''
  form.status = toNum(row.status)
  form.publishTime = row.publishTime ? dayjs(row.publishTime).valueOf() : null
  form.version = row.version
  modalVisible.value = true
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  const payload = {
    title: form.title.trim(),
    noticeType: form.noticeType,
    content: form.content.trim() || null,
    status: form.status,
    publishTime: form.publishTime ? dayjs(form.publishTime).format('YYYY-MM-DDTHH:mm:ss') : null
  }
  try {
    if (editing.value && form.id) {
      await put(`/sys/notice/${form.id}`, { ...payload, version: form.version })
      message.success('公告已保存')
    } else {
      await post('/sys/notice', payload)
      message.success('公告已新增')
    }
    await list.load()
    return true
  } catch {
    return false
  } finally {
    saving.value = false
  }
}

async function remove(row: NoticeRow): Promise<void> {
  try {
    await del(`/sys/notice/${row.id}`)
    message.success('已删除')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

/** 详情查看（正文 HTML 只以纯文本摘要呈现，避免未净化内容注入） */
const detailVisible = ref(false)
const detail = ref<NoticeRow | null>(null)

/** 详情正文：wangEditor 产出 HTML，渲染前用 DOMPurify 净化，防 XSS 注入 */
const sanitizedDetail = computed(() => DOMPurify.sanitize(detail.value?.content ?? ''))

function openDetail(row: NoticeRow): void {
  detail.value = row
  detailVisible.value = true
}

const columns = computed<DataTableColumns<NoticeRow>>(() => [
  { title: '标题', key: 'title', minWidth: 220, ellipsis: { tooltip: true } },
  {
    title: '类型',
    key: 'noticeType',
    width: 84,
    render: (row) =>
      h(NTag, { size: 'small', bordered: false, type: row.noticeType === 2 ? 'warning' : 'info' }, { default: () => noticeTypeLabel(row.noticeType) })
  },
  {
    title: '状态',
    key: 'status',
    width: 96,
    render: (row) => {
      const tag = noticeStatusTag(row.status)
      return h(NTag, { size: 'small', bordered: false, type: tag.type }, { default: () => tag.label })
    }
  },
  {
    title: '发布时间',
    key: 'publishTime',
    width: 170,
    render: (row) => formatDateTime(row.publishTime)
  },
  { title: '发布人', key: 'createByName', width: 110, render: (row) => row.createByName ?? '-' },
  { title: '创建时间', key: 'createTime', width: 170, sorter: true, render: (row) => formatDateTime(row.createTime) },
  {
    title: '操作',
    key: 'actions',
    width: 180,
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          h(NButton, { size: 'tiny', text: true, onClick: () => openDetail(row) }, { default: () => '详情' }),
          hasPerm('sys:notice:edit')
            ? h(NButton, { size: 'tiny', text: true, type: 'primary', onClick: () => openEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('sys:notice:delete')
            ? h(
                NPopconfirm,
                { onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  description: () => `删除公告「${row.title}」？`
                }
              )
            : null
        ]
      })
  }
])
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NForm inline :model="list.queryParams" label-placement="left" :show-feedback="false">
        <NSpace :size="12" align="center" wrap>
          <NFormItem label="标题">
            <NInput
              v-model:value="list.queryParams.title"
              placeholder="按标题模糊查询"
              clearable
              style="width: 220px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="类型">
            <NSelect
              v-model:value="list.queryParams.noticeType"
              :options="NOTICE_TYPE_OPTIONS"
              placeholder="全部"
              clearable
              style="width: 110px"
            />
          </NFormItem>
          <NFormItem label="状态">
            <NSelect
              v-model:value="list.queryParams.status"
              :options="NOTICE_STATUS_OPTIONS"
              placeholder="全部"
              clearable
              style="width: 120px"
            />
          </NFormItem>
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="list.reset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" style="margin: 16px 0 12px">
        <NButton v-permission="'sys:notice:add'" type="primary" @click="openCreate">新增公告</NButton>
        <span class="ps-muted">共 {{ list.total.value }} 条</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: NoticeRow) => row.id"
        :scroll-x="1120"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑公告' : '新增公告'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 720px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
      @after-enter="modalReady = true"
      @after-leave="resetForm"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="76">
        <NFormItem label="标题" path="title">
          <NInput v-model:value="form.title" maxlength="256" show-count placeholder="公告标题" />
        </NFormItem>
        <NFormItem label="类型" path="noticeType">
          <NSelect v-model:value="form.noticeType" :options="NOTICE_TYPE_OPTIONS" style="width: 160px" />
        </NFormItem>
        <NFormItem label="状态" path="status">
          <NRadioGroup v-model:value="form.status">
            <NSpace :size="16">
              <NRadio
                v-for="item in NOTICE_STATUS_OPTIONS"
                :key="item.value"
                :value="item.value"
                :label="item.label"
              />
            </NSpace>
          </NRadioGroup>
        </NFormItem>
        <NFormItem v-if="isScheduled || form.status === NOTICE_STATUS.Published" label="发布时间" path="publishTime">
          <NDatePicker
            v-model:value="form.publishTime"
            type="datetime"
            clearable
            :status="isScheduled ? 'error' : undefined"
            style="width: 240px"
            :is-date-disabled="(ts: number) => ts < Date.now() - 86400000"
          />
          <span class="ps-muted" style="margin-left: 10px; font-size: 12px">
            {{ isScheduled ? '定时发布：到点由后台作业自动置为已发布，必须晚于当前时间' : '留空 = 保存即发布时间' }}
          </span>
        </NFormItem>
        <NFormItem label="正文" path="content">
          <RichTextEditor v-if="modalReady" v-model="form.content" :min-height="260" />
        </NFormItem>
      </NForm>
    </NModal>

    <NModal
      v-model:show="detailVisible"
      preset="card"
      title="公告详情"
      style="width: 640px"
      :bordered="false"
      :segmented="{ content: true }"
    >
      <div v-if="detail" class="ps-notice-detail">
        <h3 class="ps-notice-detail__title">{{ detail.title }}</h3>
        <NSpace :size="8" align="center" class="ps-notice-detail__meta">
          <NTag size="small" :bordered="false" type="info">{{ noticeTypeLabel(detail.noticeType) }}</NTag>
          <NTag size="small" :bordered="false" :type="noticeStatusTag(detail.status).type">
            {{ noticeStatusTag(detail.status).label }}
          </NTag>
          <span class="ps-muted">发布人：{{ detail.createByName ?? '-' }}</span>
          <span class="ps-muted">发布时间：{{ formatDateTime(detail.publishTime) }}</span>
        </NSpace>
        <div v-if="detail.content" class="ps-notice-detail__body ps-rich-content" v-html="sanitizedDetail"></div>
        <div v-else class="ps-notice-detail__body ps-muted">（无正文）</div>
      </div>
    </NModal>
  </div>
</template>

<style scoped>
.ps-notice-detail__title {
  margin: 0 0 10px;
  font-size: 17px;
  font-weight: 650;
}

.ps-notice-detail__meta {
  margin-bottom: 14px;
  font-size: 12px;
}

.ps-notice-detail__body {
  max-height: 360px;
  overflow-y: auto;
  line-height: 1.8;
  font-size: 14px;
  border: 1px solid var(--ps-card-border);
  border-radius: 8px;
  padding: 12px 14px;
}

/* wangEditor 产出的富文本正文排版 */
.ps-rich-content :deep(p) {
  margin: 0 0 10px;
}

.ps-rich-content :deep(ul),
.ps-rich-content :deep(ol) {
  margin: 0 0 10px;
  padding-left: 22px;
}

.ps-rich-content :deep(img) {
  max-width: 100%;
  border-radius: 6px;
}

.ps-rich-content :deep(blockquote) {
  margin: 0 0 10px;
  padding: 6px 12px;
  border-left: 3px solid var(--ps-primary);
  color: var(--ps-text-3);
}
</style>
