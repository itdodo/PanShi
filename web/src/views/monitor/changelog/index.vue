<script setup lang="ts">
import { h, ref, type VNode } from 'vue'
import dayjs from 'dayjs'
import {
  NButton,
  NCard,
  NCode,
  NDataTable,
  NDatePicker,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NModal,
  NSpace,
  NTag,
  type DataTableColumns,
  type DataTableRowKey
} from 'naive-ui'
import { cleanChangeLogs, pageChangeLogs, type ChangeLogDto } from '@/api/admin'
import { usePageList } from '@/composables/usePageList'
import { message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'

/**
 * 变更日志（/sys/log/change）：表名/操作人/记录ID/时间区间 查询 + 行展开看字段级前后值 + 按天清理。
 * 权限：monitor:changelog:list（菜单级）/ monitor:changelog:clean。
 */
type ChangeLogFilter = {
  tableName: string
  userName: string
  recordId: string
  begin?: string
  end?: string
}

/** 日期区间 → begin/end：本地时区 ISO 串，与后端 DateTime.Now 同基准（避免 UTC 偏移一天） */
function toBegin(ms: number): string {
  return dayjs(ms).startOf('day').format('YYYY-MM-DDTHH:mm:ss')
}
function toEnd(ms: number): string {
  return dayjs(ms).endOf('day').format('YYYY-MM-DDTHH:mm:ss')
}

/** 变更条目：后端 AuditDiff 用默认 JsonSerializerOptions（键 PascalCase），此处两种命名都兼容 */
interface ChangeItem {
  field: string
  label: string
  before: string | null
  after: string | null
}

const { queryParams, loading, data, total, pagination, search, reset, load } = usePageList<ChangeLogDto, ChangeLogFilter>(
  {
    fetcher: (query) => pageChangeLogs(query),
    defaultQuery: () => ({ tableName: '', userName: '', recordId: '', begin: undefined, end: undefined }),
    pageSize: 20
  }
)

const rangeValue = ref<[number, number] | null>(null)

function onRangeUpdate(value: number | [number, number] | null): void {
  const pair = Array.isArray(value) ? value : null
  rangeValue.value = pair ? [Number(pair[0]), Number(pair[1])] : null
  queryParams.begin = pair ? toBegin(pair[0]) : undefined
  queryParams.end = pair ? toEnd(pair[1]) : undefined
}

async function onReset(): Promise<void> {
  rangeValue.value = null
  await reset()
}

/* ------------------------------ 展开行：字段级 diff ------------------------------ */
const cellStyle = 'font-size:12px;line-height:1.7;word-break:break-all'

function text(v: unknown): string | null {
  if (v === null || v === undefined) return null
  return typeof v === 'object' ? JSON.stringify(v) : String(v)
}

function parseChanges(raw?: string | null): ChangeItem[] {
  if (!raw) return []
  try {
    const parsed: unknown = JSON.parse(raw)
    if (!Array.isArray(parsed)) return []
    return parsed.map((item) => {
      const it = (item ?? {}) as Record<string, unknown>
      const field = text(it.field ?? it.Field) ?? '-'
      return {
        field,
        label: text(it.label ?? it.Label) ?? field,
        before: text(it.before ?? it.Before),
        after: text(it.after ?? it.After)
      }
    })
  } catch {
    return []
  }
}

const changeColumns: DataTableColumns<ChangeItem> = [
  {
    title: '字段',
    key: 'label',
    width: 190,
    render: (row) =>
      h('div', {}, [
        h('div', { style: 'font-size:12px;font-weight:600' }, row.label),
        h('div', { style: 'font-size:11px;color:var(--ps-text-3)' }, row.field)
      ])
  },
  {
    title: '修改前',
    key: 'before',
    minWidth: 200,
    render: (row) =>
      h(
        'span',
        { style: `${cellStyle};color:var(--ps-text-3);text-decoration:line-through` },
        row.before === null ? '（空）' : row.before
      )
  },
  {
    title: '修改后',
    key: 'after',
    minWidth: 200,
    render: (row) =>
      h('span', { style: `${cellStyle};color:#18a058;font-weight:600` }, row.after === null ? '（空）' : row.after)
  }
]

/** 原始 JSON 抽屉（diff 解析失败或想看全量时兜底） */
const rawVisible = ref(false)
const rawTitle = ref('')
const rawContent = ref('')

function showRaw(row: ChangeLogDto): void {
  rawTitle.value = `${row.tableName} · ${row.recordId}`
  rawContent.value = row.changes || '[]'
  rawVisible.value = true
}

function renderExpand(row: ChangeLogDto): VNode {
  const items = parseChanges(row.changes)
  const wrapperStyle = 'padding:8px 16px 14px;background:rgba(128,128,128,0.06)'
  if (!items.length) {
    return h(
      'div',
      { style: wrapperStyle },
      h('span', { class: 'ps-muted', style: 'font-size:12px' }, `无字段级变更记录，原始内容：${row.changes || '（空）'}`)
    )
  }
  return h('div', { style: wrapperStyle }, [
    h('div', { style: 'font-size:12px;font-weight:600;color:var(--ps-text-3);margin-bottom:6px' }, [
      `共 ${items.length} 个字段变更 `,
      h(
        NButton,
        { size: 'tiny', text: true, type: 'primary', style: 'margin-left:6px', onClick: () => showRaw(row) },
        { default: () => '查看原始 JSON' }
      )
    ]),
    h(NDataTable, {
      size: 'small',
      columns: changeColumns,
      data: items,
      bordered: false,
      singleLine: false,
      rowKey: (item: ChangeItem) => item.field
    })
  ])
}

const expandedKeys = ref<DataTableRowKey[]>([])

function onExpandChange(keys: DataTableRowKey[]): void {
  expandedKeys.value = keys
}

const columns: DataTableColumns<ChangeLogDto> = [
  { type: 'expand', width: 46, renderExpand: (row) => renderExpand(row) },
  {
    title: '表名',
    key: 'tableName',
    width: 180,
    ellipsis: { tooltip: true },
    render: (row) =>
      h(NTag, { size: 'small', bordered: false, type: 'info' }, { default: () => row.tableName || '-' })
  },
  { title: '记录ID', key: 'recordId', width: 170, render: (row) => row.recordId || '-' },
  { title: '操作人', key: 'userName', width: 120, render: (row) => row.userName || '-' },
  {
    title: '变更字段',
    key: 'changes',
    minWidth: 260,
    render: (row) => {
      const items = parseChanges(row.changes)
      if (!items.length) return h('span', { class: 'ps-muted' }, '-')
      return h(
        NSpace,
        { size: 'small', wrap: true },
        {
          default: () =>
            items
              .slice(0, 6)
              .map((it) => h(NTag, { key: it.field, size: 'small', bordered: false }, { default: () => it.label }))
              .concat(
                items.length > 6
                  ? [h(NTag, { size: 'small', bordered: false, type: 'default' }, { default: () => `+${items.length - 6}` })]
                  : []
              )
        }
      )
    }
  },
  // 钉右：本表 scroll-x 1150，窄窗口横向滚动时最后一列会被裁出可视区（「看不到时间」的真因）
  { title: '时间', key: 'createTime', width: 170, sorter: true, fixed: 'right', render: (row) => formatDateTime(row.createTime) }
]

/* -------------------------------- 清理弹窗 -------------------------------- */
const cleanVisible = ref(false)
const cleanDays = ref(90)
const cleaning = ref(false)

async function onClean(): Promise<boolean> {
  if (!cleanDays.value || cleanDays.value < 7) {
    message.warning('保留天数至少 7 天')
    return false
  }
  cleaning.value = true
  try {
    const deleted = Number(await cleanChangeLogs(cleanDays.value))
    message.success(`已清理 ${deleted} 条历史变更记录`)
    cleanVisible.value = false
    await load()
  } catch {
    /* 拦截器已提示 */
  } finally {
    cleaning.value = false
  }
  return true
}
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NForm inline :show-feedback="false" label-placement="left" class="ps-changelog__query">
        <NFormItem label="表名">
          <NInput
            v-model:value="queryParams.tableName"
            clearable
            placeholder="如 sys_user"
            style="width: 160px"
            @keyup.enter="search"
          />
        </NFormItem>
        <NFormItem label="操作人">
          <NInput
            v-model:value="queryParams.userName"
            clearable
            placeholder="用户名"
            style="width: 150px"
            @keyup.enter="search"
          />
        </NFormItem>
        <NFormItem label="记录ID">
          <NInput
            v-model:value="queryParams.recordId"
            clearable
            placeholder="业务主键"
            style="width: 190px"
            @keyup.enter="search"
          />
        </NFormItem>
        <NFormItem label="时间">
          <NDatePicker
            :value="rangeValue"
            type="daterange"
            clearable
            style="width: 260px"
            @update:value="onRangeUpdate"
          />
        </NFormItem>
        <NFormItem>
          <NSpace :size="8">
            <NButton type="primary" @click="search">查询</NButton>
            <NButton tertiary @click="onReset">重置</NButton>
          </NSpace>
        </NFormItem>
      </NForm>

      <NSpace justify="space-between" align="center" class="ps-changelog__toolbar" :wrap="true">
        <span class="ps-muted">共 {{ total }} 条变更日志 · 点击行首箭头展开字段级前后值</span>
        <NButton
          v-permission="'monitor:changelog:clean'"
          size="small"
          type="warning"
          tertiary
          @click="cleanVisible = true"
        >
          清理日志
        </NButton>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="data"
        :loading="loading"
        :pagination="pagination"
        :row-key="(row: ChangeLogDto) => row.id"
        :expanded-row-keys="expandedKeys"
        :scroll-x="1150"
        size="small"
        :bordered="false"
        @update:expanded-row-keys="onExpandChange"
      />
    </NCard>

    <NModal
      v-model:show="cleanVisible"
      preset="dialog"
      title="清理变更日志"
      type="warning"
      positive-text="确认清理"
      negative-text="取消"
      :positive-button-props="{ loading: cleaning }"
      @positive-click="onClean"
    >
      <NSpace align="center" :size="10" style="margin-top: 6px">
        <span>保留最近</span>
        <NInputNumber v-model:value="cleanDays" :min="7" :max="3650" style="width: 130px" />
        <span class="ps-muted">天（更早的变更记录将被物理删除，后端下限 7 天）</span>
      </NSpace>
    </NModal>

    <NModal v-model:show="rawVisible" preset="card" :title="`变更原始 JSON · ${rawTitle}`" style="width: 640px">
      <NCode :code="rawContent" language="json" word-wrap :trim-lines="60" style="font-size: 12px; line-height: 1.7" />
    </NModal>
  </div>
</template>

<style scoped>
.ps-changelog__query {
  margin-bottom: 6px;
  flex-wrap: wrap;
  row-gap: 10px;
}

.ps-changelog__toolbar {
  margin: 10px 0 12px;
}
</style>
