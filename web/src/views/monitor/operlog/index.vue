<script setup lang="ts">
import { h, ref } from 'vue'
import type { VNode } from 'vue'
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
  NSelect,
  NSpace,
  NTag,
  type DataTableColumns,
  type DataTableRowKey
} from 'naive-ui'
import { cleanOperLogs, exportOperLogs, pageOperLogs, type OperLogDto } from '@/api/admin'
import { usePageList } from '@/composables/usePageList'
import { message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'

/**
 * 操作日志（/sys/log/operation）：条件查询 + 行展开看参数/异常全文 + 导出 + 按天清理。
 * 权限：monitor:operlog:list（菜单级）/ monitor:operlog:export / monitor:operlog:clean。
 */
type OperLogFilter = {
  module: string
  userName: string
  /** 表单态：1 成功 / 0 失败 / null 全部（NSelect 值不支持 boolean，出参再转） */
  success: number | null
  begin?: string
  end?: string
}

const SUCCESS_OPTIONS = [
  { label: '成功', value: 1 },
  { label: '失败', value: 0 }
]

/** 日期区间 → begin/end（ISO 串用本地时区，与库里 DateTime.Now 同基准，避免 UTC 偏移一天） */
function toBegin(ms: number): string {
  return dayjs(ms).startOf('day').format('YYYY-MM-DDTHH:mm:ss')
}
function toEnd(ms: number): string {
  return dayjs(ms).endOf('day').format('YYYY-MM-DDTHH:mm:ss')
}
const toBool = (v: number | null | undefined): boolean | null => (v === 1 ? true : v === 0 ? false : null)

const { queryParams, loading, data, total, pagination, search, reset, load, applySorter } = usePageList<
  OperLogDto,
  OperLogFilter
>({
  fetcher: (query) => pageOperLogs({ ...query, success: toBool(query.success) }),
  defaultQuery: () => ({ module: '', userName: '', success: null, begin: undefined, end: undefined }),
  pageSize: 20
})

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

/* --------------------------------- 导出 --------------------------------- */
const exporting = ref(false)

async function onExport(): Promise<void> {
  exporting.value = true
  try {
    const { module, userName, success, begin, end } = queryParams
    // 导出不受前端分页影响：后端固定 Top 5000，这里只带上过滤条件
    await exportOperLogs({ pageNum: 1, pageSize: 200, module, userName, begin, end, success: toBool(success) })
    message.success('导出文件已开始下载')
  } catch {
    /* 拦截器已提示 */
  } finally {
    exporting.value = false
  }
}

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
    const deleted = Number(await cleanOperLogs(cleanDays.value))
    message.success(`已清理 ${deleted} 条历史日志`)
    cleanVisible.value = false
    await load()
  } catch {
    /* 拦截器已提示 */
  } finally {
    cleaning.value = false
  }
  return true
}

/* -------------------------------- 表格列 -------------------------------- */
const expandedKeys = ref<DataTableRowKey[]>([])

/** 展开行：请求参数 / 异常信息全文（NCode 保换行、可选中复制） */
function renderExpand(row: OperLogDto): VNode {
  const codeStyle = 'font-size:12px;line-height:1.7'
  const labelStyle = 'font-size:12px;font-weight:600;color:var(--ps-text-3);margin:10px 0 4px'
  const children = [
    h('div', { style: labelStyle }, '请求参数'),
    h(NCode, {
      code: row.params?.trim() || '（无参数）',
      language: 'json',
      wordWrap: true,
      trimLines: 30,
      style: codeStyle
    })
  ]
  if (row.errorMsg) {
    children.push(h('div', { style: `${labelStyle};color:#d03050` }, '异常信息'))
    children.push(h(NCode, { code: row.errorMsg, wordWrap: true, trimLines: 30, style: codeStyle }))
  }
  return h('div', { style: 'padding:0 16px 12px;background:rgba(128,128,128,0.06)' }, children)
}

const columns: DataTableColumns<OperLogDto> = [
  { type: 'expand', width: 46, renderExpand: (row) => renderExpand(row) },
  { title: '模块', key: 'module', width: 120, ellipsis: { tooltip: true } },
  { title: '操作', key: 'action', width: 130, ellipsis: { tooltip: true } },
  {
    title: '方式',
    key: 'method',
    width: 84,
    render: (row) => h(NTag, { size: 'small', type: 'info', bordered: false }, { default: () => row.method || '-' })
  },
  { title: '请求地址', key: 'url', minWidth: 220, ellipsis: { tooltip: true } },
  { title: '操作人', key: 'userName', width: 110, render: (row) => row.userName || '-' },
  { title: 'IP', key: 'ip', width: 130, render: (row) => row.ip || '-' },
  {
    title: '耗时',
    key: 'elapsedMs',
    width: 96,
    sorter: true,
    render: (row) => `${Number(row.elapsedMs || 0)} ms`
  },
  {
    title: '结果',
    key: 'success',
    width: 88,
    render: (row) =>
      h(
        NTag,
        { size: 'small', type: row.success ? 'success' : 'error', bordered: false },
        { default: () => (row.success ? '成功' : '失败') }
      )
  },
  { title: '时间', key: 'createTime', width: 170, sorter: true, render: (row) => formatDateTime(row.createTime) }
]

function onExpandChange(keys: DataTableRowKey[]): void {
  expandedKeys.value = keys
}
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NForm inline :show-feedback="false" label-placement="left" class="ps-log__query">
        <NFormItem label="模块">
          <NInput
            v-model:value="queryParams.module"
            clearable
            placeholder="模块名"
            style="width: 150px"
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
        <NFormItem label="结果">
          <NSelect
            v-model:value="queryParams.success"
            clearable
            :options="SUCCESS_OPTIONS"
            placeholder="全部"
            style="width: 120px"
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

      <NSpace justify="space-between" align="center" class="ps-log__toolbar" :wrap="true">
        <span class="ps-muted">共 {{ total }} 条操作日志 · 点击行首箭头可展开查看参数全文</span>
        <NSpace :size="8">
          <NButton v-permission="'monitor:operlog:export'" size="small" :loading="exporting" @click="onExport">
            导出 Excel
          </NButton>
          <NButton
            v-permission="'monitor:operlog:clean'"
            size="small"
            type="warning"
            tertiary
            @click="cleanVisible = true"
          >
            清理日志
          </NButton>
        </NSpace>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="data"
        :loading="loading"
        :pagination="pagination"
        :row-key="(row: OperLogDto) => row.id"
        :expanded-row-keys="expandedKeys"
        :scroll-x="1300"
        size="small"
        :bordered="false"
        @update:expanded-row-keys="onExpandChange"
        @update:sorter="applySorter"
      />
    </NCard>

    <NModal
      v-model:show="cleanVisible"
      preset="dialog"
      title="清理操作日志"
      type="warning"
      positive-text="确认清理"
      negative-text="取消"
      :positive-button-props="{ loading: cleaning }"
      @positive-click="onClean"
    >
      <NSpace align="center" :size="10" style="margin-top: 6px">
        <span>保留最近</span>
        <NInputNumber v-model:value="cleanDays" :min="7" :max="3650" style="width: 130px" />
        <span class="ps-muted">天（更早的日志将被物理删除，后端下限 7 天）</span>
      </NSpace>
    </NModal>
  </div>
</template>

<style scoped>
.ps-log__query {
  margin-bottom: 6px;
  flex-wrap: wrap;
  row-gap: 10px;
}

.ps-log__toolbar {
  margin: 10px 0 12px;
}
</style>
