<script setup lang="ts">
import { computed, h, onActivated, onDeactivated, onMounted, onUnmounted, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
  NInput,
  NPopconfirm,
  NSpace,
  NSwitch,
  NTag,
  NTooltip,
  type DataTableColumns
} from 'naive-ui'
import { kickOnline, listOnline, type OnlineDto } from '@/api/admin'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDateTime, shortUserAgent } from '@/utils/format'

/**
 * 在线用户（/monitor/online）：后端返回**数组**（非分页），关键字过滤 + 强退 + 可选轮询。
 * 权限：monitor:online:list（菜单级）/ monitor:online:kick。当前会话标「本机」且禁止强退。
 */
const keyword = ref('')
const rows = ref<OnlineDto[]>([])
const loading = ref(false)
const autoRefresh = ref(true)
const kicking = ref<string | null>(null)
const canKick = computed(() => hasPerm('monitor:online:kick'))

let timer = 0

function stopTimer(): void {
  if (timer) {
    window.clearInterval(timer)
    timer = 0
  }
}

function startTimer(): void {
  stopTimer()
  if (!autoRefresh.value) return
  // 静默轮询：失败只留旧数据，不重复刷提示
  timer = window.setInterval(() => void load(true), 10000)
}

async function load(silent = false): Promise<void> {
  loading.value = !silent
  try {
    const data = await listOnline(keyword.value.trim() || undefined)
    rows.value = Array.isArray(data) ? data : []
  } catch {
    if (!silent) rows.value = []
  } finally {
    loading.value = false
  }
}

async function onKick(row: OnlineDto): Promise<void> {
  kicking.value = row.id
  try {
    await kickOnline(row.id)
    message.success(`已强制下线 ${row.userName || row.loginIp || '该会话'}`)
    await load(true)
  } catch {
    /* 拦截器已提示 */
  } finally {
    kicking.value = null
  }
}

const columns: DataTableColumns<OnlineDto> = [
  {
    title: '用户名',
    key: 'userName',
    width: 180,
    render: (row) =>
      h('div', { style: 'display:flex;align-items:center;gap:6px' }, [
        h('span', row.userName || `用户#${row.userId}`),
        row.current ? h(NTag, { size: 'small', type: 'success', bordered: false }, { default: () => '本机' }) : null
      ])
  },
  { title: '会话ID', key: 'id', width: 170, render: (row) => h('span', { class: 'ps-muted' }, row.id) },
  { title: '登录IP', key: 'loginIp', width: 140, render: (row) => row.loginIp || '-' },
  {
    title: '客户端',
    key: 'userAgent',
    minWidth: 200,
    ellipsis: { tooltip: true },
    render: (row) =>
      row.userAgent
        ? h(
            NTooltip,
            { trigger: 'hover' },
            { trigger: () => h('span', shortUserAgent(row.userAgent)), default: () => row.userAgent }
          )
        : h('span', { class: 'ps-muted' }, '未知设备')
  },
  { title: '登录时间', key: 'createTime', width: 170, render: (row) => formatDateTime(row.createTime) },
  { title: '会话过期', key: 'expireTime', width: 170, render: (row) => formatDateTime(row.expireTime) },
  {
    title: '操作',
    key: 'actions',
    width: 96,
    fixed: 'right',
    render: (row) => {
      if (!canKick.value) return h('span', { class: 'ps-muted' }, '—')
      if (row.current) {
        return h(
          NTooltip,
          null,
          { trigger: () => h('span', { class: 'ps-muted' }, '不可强退'), default: () => '这是你当前浏览器所在的会话' }
        )
      }
      return h(
        NPopconfirm,
        { onPositiveClick: () => onKick(row) },
        {
          trigger: () =>
            h(
              NButton,
              { size: 'tiny', type: 'error', tertiary: true, loading: kicking.value === row.id },
              { default: () => '强退' }
            ),
          default: () => `确认强制下线 ${row.userName || row.loginIp || '该会话'}？其访问令牌立即失效。`
        }
      )
    }
  }
]

function onAutoRefreshChange(v: boolean): void {
  autoRefresh.value = v
  if (v) startTimer()
  else stopTimer()
}

onMounted(() => {
  void load()
  startTimer()
})
// 页签缓存（keep-alive）：切走停轮询，切回续轮询并立即刷新一次
onActivated(() => {
  startTimer()
  void load(true)
})
onDeactivated(stopTimer)
onUnmounted(stopTimer)
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NSpace justify="space-between" align="center" :wrap="true" class="ps-online__bar">
        <NSpace :size="8" align="center">
          <NInput
            v-model:value="keyword"
            clearable
            placeholder="用户名 / IP 关键字"
            style="width: 220px"
            @keyup.enter="load()"
            @clear="load()"
          />
          <NButton type="primary" @click="load()">查询</NButton>
          <NButton tertiary :loading="loading" @click="load()">刷新</NButton>
        </NSpace>
        <NSpace :size="10" align="center">
          <span class="ps-muted">在线会话 {{ rows.length }} 个</span>
          <NSwitch :value="autoRefresh" size="small" @update:value="onAutoRefreshChange">
            <template #checked>10s 自动刷新</template>
            <template #unchecked>自动刷新关</template>
          </NSwitch>
        </NSpace>
      </NSpace>

      <NDataTable
        :columns="columns"
        :data="rows"
        :loading="loading"
        :row-key="(row: OnlineDto) => row.id"
        :scroll-x="1180"
        size="small"
        :bordered="false"
        :pagination="false"
      />
      <p class="ps-muted ps-online__tip">
        强退只作废对应会话的令牌（该用户下一次请求即被踢回登录页），不影响其其它设备的会话。
      </p>
    </NCard>
  </div>
</template>

<style scoped>
.ps-online__bar {
  margin-bottom: 12px;
  row-gap: 10px;
}

.ps-online__tip {
  margin: 10px 0 0;
  font-size: 12px;
}
</style>
