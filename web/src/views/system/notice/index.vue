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
import { createNotice, deleteNotice, pageNotices, updateNotice, type NoticeDto } from '@/api/notice'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'
import RichTextEditor from '@/components/RichTextEditor.vue'
import { NOTICE_STATUS, NOTICE_STATUS_OPTIONS, NOTICE_TYPE_OPTIONS, noticeStatusTag, noticeTypeLabel, toNum } from '../_shared'

/**
 * 公告管理（/sys/notice）：分页 + CRUD + 三态状态（0 停用/草稿 / 1 已发布 / 2 定时发布）。
 * 后端 NoticeDto = { id, title, noticeType, content, status, publishTime, createByName, createTime, version }；
 * 新增 POST /sys/notice、编辑 PUT /sys/notice/{id}（必带 version）、删除 DELETE /sys/notice/{id}。
 * 交互：新建不出现「状态」单选，改由底部按钮直接表态——存草稿(0) / 立即发布(1) / 选了发布时间即定时发布(2)；
 * 编辑仍保留状态单选（可把草稿转发布、把定时改回草稿等），「发布时间」仅在定时语义下出现并必填。
 * 定时到期由后台作业 sys.notice.publish（cron * * * * *，每分钟）置 2→1；
 * 「已发布且发布时间留空」由后端 Apply 自动补 now，故立即发布无需手填时间。
 * 正文用 wangEditor 富文本（红线 #10：弹层 after-enter 后才挂载编辑器）；详情渲染前经 DOMPurify 净化防 XSS。
 */
type NoticeRow = NoticeDto

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
  fetcher: pageNotices,
  defaultQuery: () => ({ title: '', noticeType: null, status: null })
})

const modalVisible = ref(false)
/** 红线 #10：富文本编辑器仅在弹层动画结束后挂载（可见容器内初始化） */
const modalReady = ref(false)
const saving = ref(false)
const editing = computed(() => !!form.id)
/**
 * 是否处于「定时发布」语义：
 * 新建时看有没有选发布时间（选了=定时，没选=立即发布），编辑时看状态单选。
 * 「必须晚于当前」这条规则只在定时语义下生效，否则编辑一条已发布公告会被自己过去的发布时间卡住。
 */
const scheduling = computed(() =>
  editing.value ? form.status === NOTICE_STATUS.Scheduled : form.publishTime !== null
)
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

/** 新建时主按钮文案：选了时间就是定时发布 */
const publishLabel = computed(() => (form.publishTime ? '定时发布' : '立即发布'))

const rules = computed<FormRules>(() => ({
  title: [{ required: true, max: 256, message: '请输入公告标题', trigger: ['input', 'blur'] }],
  publishTime: [
    {
      validator: (_rule: unknown, value: number | null) => {
        if (!scheduling.value) return true
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

/**
 * 提交。新建时状态由点的按钮决定（存草稿=0 / 立即发布=1 / 选了时间=定时2），编辑时沿用状态单选。
 * 草稿不带发布时间，避免留下一条无意义的未来时间。
 */
async function submit(status?: number): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  const nextStatus = status ?? form.status
  const nextTime = nextStatus === NOTICE_STATUS.Stopped ? null : form.publishTime
  const payload = {
    title: form.title.trim(),
    noticeType: form.noticeType,
    content: form.content.trim() || null,
    status: nextStatus,
    publishTime: nextTime ? dayjs(nextTime).format('YYYY-MM-DDTHH:mm:ss') : null
  }
  try {
    if (editing.value && form.id) {
      await updateNotice(form.id, { ...payload, version: form.version })
      message.success('公告已保存')
    } else {
      await createNotice(payload)
      message.success(
        nextStatus === NOTICE_STATUS.Published
          ? '公告已发布'
          : nextStatus === NOTICE_STATUS.Scheduled
            ? '公告已排期，到点自动发布'
            : '公告已存为草稿'
      )
    }
    await list.load()
    // 自定义 #action 后弹窗不再自动关闭（原先靠 dialog 预设的正按钮），成功即收起
    modalVisible.value = false
    return true
  } catch {
    return false
  } finally {
    saving.value = false
  }
}

/** 新建·主按钮：选了发布时间即视为定时发布，否则立即发布 */
function submitPublish(): Promise<boolean> {
  return submit(form.publishTime ? NOTICE_STATUS.Scheduled : NOTICE_STATUS.Published)
}

/** 新建·存草稿 */
function submitDraft(): Promise<boolean> {
  return submit(NOTICE_STATUS.Stopped)
}

async function remove(row: NoticeRow): Promise<void> {
  try {
    await deleteNotice(row.id)
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
      const tag = noticeStatusTag(row.status, row.publishTime)
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
                  default: () => `删除公告「${row.title}」？`
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
      style="width: 720px"
      :auto-focus="false"
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
        <!-- 新建时用底部按钮表达意图（草稿/立即发布/定时），不再让状态单选和按钮重复表态 -->
        <NFormItem v-if="editing" label="状态" path="status">
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
        <NFormItem v-if="!editing || scheduling" label="发布时间" path="publishTime">
          <NDatePicker
            v-model:value="form.publishTime"
            type="datetime"
            clearable
            style="width: 240px"
            :is-date-disabled="(ts: number) => ts < Date.now() - 86400000"
          />
          <span class="ps-muted" style="margin-left: 10px; font-size: 12px">
            {{ editing ? '定时发布：到点由后台作业自动置为已发布，必须晚于当前时间' : '留空 = 立即发布；选时间 = 到点自动发布' }}
          </span>
        </NFormItem>
        <NFormItem label="正文" path="content">
          <RichTextEditor v-if="modalReady" v-model="form.content" :min-height="260" />
        </NFormItem>
      </NForm>
      <template #action>
        <!-- size=small 与 dialog 预设正/负按钮一致（Dialog.mjs 里写死 size:'small'，自定义槽不会继承） -->
        <NSpace :size="8">
          <NButton size="small" :disabled="saving" tertiary @click="modalVisible = false">取消</NButton>
          <template v-if="editing">
            <NButton size="small" type="primary" :loading="saving" @click="submit()">保存</NButton>
          </template>
          <template v-else>
            <NButton size="small" :disabled="saving" tertiary @click="submitDraft">存草稿</NButton>
            <NButton size="small" type="primary" :loading="saving" @click="submitPublish">{{ publishLabel }}</NButton>
          </template>
        </NSpace>
      </template>
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
          <NTag size="small" :bordered="false" :type="noticeStatusTag(detail.status, detail.publishTime).type">
            {{ noticeStatusTag(detail.status, detail.publishTime).label }}
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
