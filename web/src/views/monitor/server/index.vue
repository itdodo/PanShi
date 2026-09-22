<script setup lang="ts">
import { computed, onActivated, onDeactivated, onMounted, onUnmounted, ref } from 'vue'
import { NAlert, NButton, NCard, NGrid, NGridItem, NSpace, NTag, NTooltip } from 'naive-ui'
import { serverInfo, type ServerInfo } from '@/api/admin'
import AppIcon from '@/components/AppIcon.vue'
import { formatDateTime } from '@/utils/format'

/**
 * 服务监控（/monitor/server）：进程与数据库基本信息，5s 轮询（页签切走暂停、连续失败自动停止）。
 * 权限：monitor:server:list（菜单级，接口仅需登录）。
 */
const REFRESH_MS = 5000
const MAX_FAILS = 3

const info = ref<ServerInfo | null>(null)
const loading = ref(false)
const polling = ref(true)
const failCount = ref(0)
const lastSync = ref<string>('')
let timer = 0

function stopTimer(): void {
  if (timer) {
    window.clearInterval(timer)
    timer = 0
  }
}

function startTimer(): void {
  stopTimer()
  if (!polling.value) return
  timer = window.setInterval(() => void load(true), REFRESH_MS)
}

async function load(silent = false): Promise<void> {
  if (loading.value) return
  loading.value = !silent
  try {
    const data = await serverInfo()
    info.value = data ?? null
    lastSync.value = new Date().toLocaleTimeString('zh-CN', { hour12: false })
    failCount.value = 0
  } catch {
    failCount.value += 1
    // 连续失败即暂停轮询，避免拦截器反复弹错
    if (failCount.value >= MAX_FAILS) {
      polling.value = false
      stopTimer()
    }
  } finally {
    loading.value = false
  }
}

function onResume(): void {
  failCount.value = 0
  polling.value = true
  void load()
  startTimer()
}

/** 已运行分钟数 → 「x 天 y 小时 z 分钟」 */
const uptimeText = computed(() => {
  const min = Number(info.value?.uptimeMin ?? 0)
  if (!Number.isFinite(min) || min < 0) return '-'
  const d = Math.floor(min / 1440)
  const h = Math.floor((min % 1440) / 60)
  const m = Math.floor(min % 60)
  const parts: string[] = []
  if (d) parts.push(`${d} 天`)
  if (h) parts.push(`${h} 小时`)
  parts.push(`${m} 分钟`)
  return parts.join(' ')
})

/** 托管堆占工作集比例（内存压力粗略指标，仅可视化） */
const heapRatio = computed(() => {
  const working = Number(info.value?.memWorkingSetMb ?? 0)
  const heap = Number(info.value?.memHeapMb ?? 0)
  if (!working || !heap) return 0
  return Math.min(100, Math.round((heap / working) * 100))
})

const cards = computed(() => {
  const data = info.value
  return [
    { key: 'machine', icon: 'lucide:server', label: '主机名', value: data?.machineName || '-', hint: 'Environment.MachineName' },
    { key: 'os', icon: 'lucide:monitor-smartphone', label: '操作系统', value: data?.os || '-', hint: 'RuntimeInformation.OSDescription' },
    { key: 'framework', icon: 'lucide:binary', label: '运行时', value: data?.framework || '-', hint: '.NET 版本' },
    { key: 'cpu', icon: 'lucide:cpu', label: 'CPU 逻辑核', value: `${Number(data?.cpuCores ?? 0)} 核`, hint: 'Environment.ProcessorCount' },
    {
      key: 'working',
      icon: 'lucide:memory-stick',
      label: '进程工作集',
      value: `${Number(data?.memWorkingSetMb ?? 0).toFixed(1)} MB`,
      hint: '进程占用物理内存'
    },
    {
      key: 'heap',
      icon: 'lucide:database-backup',
      label: '托管堆',
      value: `${Number(data?.memHeapMb ?? 0).toFixed(1)} MB`,
      hint: `GC.GetTotalMemory · 占工作集约 ${heapRatio.value}%`
    },
    { key: 'pg', icon: 'lucide:database', label: 'PostgreSQL 版本', value: data?.pgVersion || '-', hint: 'show server_version' },
    { key: 'dbSize', icon: 'lucide:hard-drive', label: '当前库大小', value: data?.dbSize || '-', hint: 'pg_size_pretty(pg_database_size())' },
    { key: 'startup', icon: 'lucide:calendar-clock', label: '启动时间', value: formatDateTime(data?.startupTime), hint: '进程 StartTime' },
    { key: 'uptime', icon: 'lucide:timer', label: '已运行', value: uptimeText.value, hint: `${Number(data?.uptimeMin ?? 0).toFixed(1)} 分钟` }
  ]
})

onMounted(() => {
  void load()
  startTimer()
})
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
      <template #header>
        <NSpace align="center" justify="space-between" :wrap="true">
          <NSpace align="center" :size="8">
            <AppIcon name="lucide:gauge" :size="18" />
            <span>服务监控</span>
            <NTag size="small" :bordered="false" type="info">5 秒自动刷新</NTag>
            <span v-if="lastSync" class="ps-muted ps-server__sync">最近同步 {{ lastSync }}</span>
          </NSpace>
          <NSpace :size="8" align="center">
            <NButton size="small" tertiary :loading="loading" @click="load()">
              <template #icon><AppIcon name="lucide:refresh-cw" :size="14" /></template>
              刷新
            </NButton>
          </NSpace>
        </NSpace>
      </template>

      <NAlert v-if="!polling" type="warning" :bordered="false" class="ps-server__alert" title="已暂停自动刷新">
        连续 {{ failCount }} 次拉取 /monitor/server 失败（后端可能未启动）。
        <NButton size="tiny" type="warning" tertiary style="margin-left: 8px" @click="onResume">恢复刷新</NButton>
      </NAlert>

      <NGrid responsive="screen" cols="1 s:2 m:3 l:4" :x-gap="12" :y-gap="12">
        <NGridItem v-for="card in cards" :key="card.key">
          <div class="ps-server__cell">
            <div class="ps-server__icon"><AppIcon :name="card.icon" :size="18" /></div>
            <div class="ps-server__body">
              <NTooltip trigger="hover">
                <template #trigger>
                  <div class="ps-server__label">{{ card.label }}</div>
                </template>
                {{ card.hint }}
              </NTooltip>
              <div class="ps-server__value">{{ card.value }}</div>
            </div>
          </div>
        </NGridItem>
      </NGrid>

      <div class="ps-server__bar">
        <div class="ps-server__bar-head">
          <span class="ps-muted">托管堆 / 工作集</span>
          <span class="ps-muted">{{ heapRatio }}%</span>
        </div>
        <div class="ps-server__track"><div class="ps-server__fill" :style="{ width: `${heapRatio}%` }" /></div>
      </div>

      <p class="ps-muted ps-server__tip">
        这里只暴露进程级指标（.NET 侧无跨平台 CPU 负载读取）；机器级 CPU/网络/磁盘请配合 Hangfire 面板与数据库慢查询日志排查。
      </p>
    </NCard>
  </div>
</template>

<style scoped>
.ps-server__alert {
  margin-bottom: 14px;
}

.ps-server__sync {
  font-size: 12px;
}

.ps-server__cell {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 14px;
  border: 1px solid rgba(128, 128, 128, 0.18);
  border-radius: 12px;
  background: rgba(128, 128, 128, 0.03);
}

.ps-server__icon {
  display: grid;
  place-items: center;
  width: 34px;
  height: 34px;
  flex: none;
  border-radius: 10px;
  background: var(--ps-primary-soft);
  color: var(--ps-primary);
}

.ps-server__body {
  min-width: 0;
}

.ps-server__label {
  font-size: 12px;
  color: var(--ps-text-3);
  cursor: default;
}

.ps-server__value {
  margin-top: 4px;
  font-size: 15px;
  font-weight: 600;
  line-height: 1.5;
  word-break: break-word;
}

.ps-server__bar {
  margin-top: 16px;
}

.ps-server__bar-head {
  display: flex;
  justify-content: space-between;
  font-size: 12px;
  margin-bottom: 6px;
}

.ps-server__track {
  height: 8px;
  border-radius: 8px;
  background: rgba(128, 128, 128, 0.16);
  overflow: hidden;
}

.ps-server__fill {
  height: 100%;
  border-radius: 8px;
  background: linear-gradient(90deg, #2563eb, #18a058);
  transition: width 0.4s ease;
}

.ps-server__tip {
  margin: 14px 0 0;
  font-size: 12px;
}
</style>
