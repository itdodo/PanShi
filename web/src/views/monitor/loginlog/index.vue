<script setup lang="ts">
import { h, ref } from 'vue'
import dayjs from 'dayjs'
import {
  NButton,
  NCard,
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
  NTooltip,
  type DataTableColumns
} from 'naive-ui'
import { cleanLoginLogs, exportLoginLogs, pageLoginLogs, type LoginLogDto } from '@/api/admin'
import { usePageList } from '@/composables/usePageList'
import { message } from '@/utils/feedback'
import { formatDateTime, shortUserAgent } from '@/utils/format'

/**
 * 登录日志（/sys/log/login）：用户名/结果/时间区间查询 + 导出 + 按天清理。
 * 权限：monitor:loginlog:list（菜单级）/ monitor:loginlog:export / monitor:loginlog:clean。
 */
type LoginLogFilter = {
  userName: string
  /** 表单态：1 成功 / 0 失败 / null 全部（NSelect 不接受 boolean，出参再转） */
  success: number | null
  begin?: string
  end?: string
}

const SUCCESS_OPTIONS = [
  { label: '成功', value: 1 },
  { label: '失败', value: 0 }
]

/** 日期区间 → begin/end：本地时区 ISO 串，与后端 DateTime.Now 同基准（避免 UTC 偏移一天） */
function toBegin(ms: number): string {
  return dayjs(ms).startOf('day').format('YYYY-MM-DDTHH:mm:ss')
}
function toEnd(ms: number): string {
  return dayjs(ms).endOf('day').format('YYYY-MM-DDTHH:mm:ss')
}
const toBool = (v: number | null | undefined): boolean | null => (v === 1 ? true : v === 0 ? false : null)

const { queryParams, loading, data, total, pagination, search, reset, load, applySorter } = usePageList<
  LoginLogDto,
  LoginLogFilter
>({
  fetcher: (query) => pageLoginLogs({ ...query, success: toBool(query.success) }),
  defaultQuery: () => ({ userName: '', success: null, begin: undefined, end: undefined }),
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
    const { userName, success, begin, end } = queryParams
    // 后端固定 Top 5000：这里只带过滤条件，分页参数无意义
    await exportLoginLogs({ pageNum: 1, pageSize: 200, userName, begin, end, success: toBool(success) })
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
    const deleted = Number(await cleanLoginLogs(cleanDays.value))
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
const columns: DataTableColumns<LoginLogDto> = [
  { title: '用户名', key: 'userName', width: 140, render: (row) => row.userName || '-' },
  {
    title: '结果描述',
    key: 'result',
    minWidth: 180,
    ellipsis: { tooltip: true },
    render: (row) => row.result || (row.success ? '登录成功' : '登录失败')
  },
  {
    title: '状态',
    key: 'success',
    width: 96,
    render: (row) =>
      h(
        NTag,
        { size: 'small', type: row.success ? 'success' : 'error', bordered: false },
        { default: () => (row.success ? '成功' : '失败') }
      )
  },
  { title: 'IP', key: 'ip', width: 140, render: (row) => row.ip || '-' },
  {
    title: '设备',
    key: 'userAgent',
    minWidth: 180,
    ellipsis: { tooltip: true },
    render: (row) =>
      row.userAgent
        ? h(NTooltip, { trigger: 'hover' }, {
            trigger: () => h('span', shortUserAgent(row.userAgent)),
            default: () => row.userAgent
          })
        : h('span', { class: 'ps-muted' }, '未知设备')
  },
  { title: '登录时间', key: 'createTime', width: 170, sorter: true, render: (row) => formatDateTime(row.createTime) },
  { title: '日志ID', key: 'id', width: 170, render: (row) => h('span', { class: 'ps-muted' }, row.id) }
]
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NForm inline :show-feedback="false" label-placement="left" class="ps-loginlog__query">
        <NFormItem label="用户名">
          <NInput
            v-model:value="queryParams.userName"
            clearable
            placeholder="登录名"
            style="width: 160px"
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

      <NSpace justify="space-between" align="center" class="ps-loginlog__toolbar" :wrap="true">
        <span class="ps-muted">共 {{ total }} 条登录日志</span>
        <NSpace :size="8">
          <NButton v-permission="'monitor:loginlog:export'" size="small" :loading="exporting" @click="onExport">
            导出 Excel
          </NButton>
          <NButton
            v-permission="'monitor:loginlog:clean'"
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
        :row-key="(row: LoginLogDto) => row.id"
        :scroll-x="1150"
        size="small"
        :bordered="false"
        @update:sorter="applySorter"
      />
    </NCard>

    <NModal
      v-model:show="cleanVisible"
      preset="dialog"
      title="清理登录日志"
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
.ps-loginlog__query {
  margin-bottom: 6px;
  flex-wrap: wrap;
  row-gap: 10px;
}

.ps-loginlog__toolbar {
  margin: 10px 0 12px;
}
</style>
