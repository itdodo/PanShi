<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { NAlert, NButton, NCard, NEmpty, NGrid, NGridItem, NSpace, NSpin, NSwitch, NTag } from 'naive-ui'
import {
  hangfireConsoleUrl,
  listJobs,
  toggleJob,
  triggerJob,
  type JobDto
} from '@/api/admin'
import AppIcon from '@/components/AppIcon.vue'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'

/**
 * 定时任务（/monitor/jobs）：Hangfire 重复任务清单卡片栅格，支持启停与立即执行。
 * 权限：monitor:job:list（菜单级）/ monitor:job:manage 控制启停与执行按钮。
 * 高级日志（执行历史/失败重试）在 Hangfire 面板，本页面只做日常操作。
 */
const jobs = ref<JobDto[]>([])
const loading = ref(false)
/** 正在操作的任务 id（同一时刻只允许一个写操作，避免误点） */
const busyId = ref<string | null>(null)
const canManage = computed(() => hasPerm('monitor:job:manage'))

async function load(): Promise<void> {
  loading.value = true
  try {
    const data = await listJobs()
    jobs.value = Array.isArray(data) ? data : []
  } catch {
    jobs.value = []
  } finally {
    loading.value = false
  }
}

async function onToggle(job: JobDto, enable: boolean): Promise<void> {
  busyId.value = job.jobId
  try {
    await toggleJob(job.jobId, enable)
    job.enabled = enable
    message.success(`「${job.name}」已${enable ? '启用' : '停用'}`)
  } catch {
    /* 拦截器已提示：状态未变，无需回滚 */
  } finally {
    busyId.value = null
  }
}

async function onTrigger(job: JobDto): Promise<void> {
  busyId.value = job.jobId
  try {
    await triggerJob(job.jobId)
    message.success(`「${job.name}」已加入执行队列，稍后可在 Hangfire 面板查看结果`)
  } catch {
    /* 拦截器已提示 */
  } finally {
    busyId.value = null
  }
}

onMounted(() => void load())
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <template #header>
        <NSpace align="center" justify="space-between" :wrap="true">
          <NSpace align="center" :size="8">
            <AppIcon name="lucide:timer" :size="18" />
            <span>定时任务</span>
            <NTag size="small" :bordered="false" type="info">{{ jobs.length }} 个</NTag>
          </NSpace>
          <NButton size="small" tertiary :loading="loading" @click="load">刷新</NButton>
        </NSpace>
      </template>

      <NAlert v-if="!canManage" type="info" :bordered="false" class="ps-job__alert">
        当前账号只有查看权限（monitor:job:list），启停与立即执行需要 monitor:job:manage。
      </NAlert>

      <NSpin :show="loading">
        <NEmpty v-if="!loading && !jobs.length" description="后端未注册任何重复任务" style="padding: 40px 0" />
        <NGrid v-else responsive="screen" cols="1 m:2 l:3" :x-gap="14" :y-gap="14">
          <NGridItem v-for="job in jobs" :key="job.jobId">
            <div class="ps-job__card">
              <div class="ps-job__head">
                <div class="ps-job__icon" :style="{ color: job.enabled ? '#18a058' : 'var(--ps-text-3)' }">
                  <AppIcon :name="job.enabled ? 'lucide:play-circle' : 'lucide:pause-circle'" :size="20" />
                </div>
                <div class="ps-job__title">
                  <div class="ps-job__name">{{ job.name }}</div>
                  <div class="ps-job__id">{{ job.jobId }}</div>
                </div>
                <NTag :type="job.enabled ? 'success' : 'default'" size="small" :bordered="false">
                  {{ job.enabled ? '运行中' : '已停用' }}
                </NTag>
              </div>

              <dl class="ps-job__meta">
                <dt>Cron</dt>
                <dd><code>{{ job.cron }}</code></dd>
                <dt>调度</dt>
                <dd>{{ job.scheduler || '—' }}</dd>
              </dl>

              <NSpace align="center" :size="12" class="ps-job__actions">
                <NSpace align="center" :size="8">
                  <span class="ps-muted">启停</span>
                  <NSwitch
                    :value="job.enabled"
                    :disabled="!canManage || busyId === job.jobId"
                    :loading="busyId === job.jobId"
                    size="small"
                    @update:value="(v: boolean) => onToggle(job, v)"
                  />
                </NSpace>
                <NButton
                  v-if="canManage"
                  size="tiny"
                  type="primary"
                  tertiary
                  :disabled="busyId !== null && busyId !== job.jobId"
                  :loading="busyId === job.jobId"
                  @click="onTrigger(job)"
                >
                  立即执行
                </NButton>
              </NSpace>
            </div>
          </NGridItem>
        </NGrid>
      </NSpin>

      <div class="ps-job__foot">
        <span class="ps-muted">
          任务由 Hangfire 调度（服务器本地时区）。执行历史、失败重试、队列吞吐等高级日志请打开
        </span>
        <NButton tag="a" :href="hangfireConsoleUrl" target="_blank" rel="noopener" text type="primary">
          Hangfire 面板
        </NButton>
        <span class="ps-muted">查看。</span>
      </div>
    </NCard>
  </div>
</template>

<style scoped>
.ps-job__alert {
  margin-bottom: 14px;
}

.ps-job__card {
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  border: 1px solid rgba(128, 128, 128, 0.18);
  border-radius: 12px;
  background: rgba(128, 128, 128, 0.03);
}

.ps-job__head {
  display: flex;
  align-items: center;
  gap: 10px;
}

.ps-job__icon {
  display: grid;
  place-items: center;
  flex: none;
}

.ps-job__title {
  flex: 1 1 auto;
  min-width: 0;
}

.ps-job__name {
  font-size: 15px;
  font-weight: 600;
}

.ps-job__id {
  margin-top: 2px;
  font-size: 12px;
  color: var(--ps-text-3);
  word-break: break-all;
}

.ps-job__meta {
  display: grid;
  grid-template-columns: 46px 1fr;
  gap: 6px 10px;
  margin: 0;
  font-size: 13px;
}

.ps-job__meta dt {
  color: var(--ps-text-3);
}

.ps-job__meta dd {
  margin: 0;
}

.ps-job__meta code {
  font-size: 12px;
  padding: 1px 6px;
  border-radius: 4px;
  background: rgba(128, 128, 128, 0.12);
}

.ps-job__actions {
  margin-top: auto;
  justify-content: space-between;
}

.ps-job__foot {
  margin-top: 18px;
  padding-top: 12px;
  border-top: 1px solid rgba(128, 128, 128, 0.16);
  font-size: 13px;
}
</style>
