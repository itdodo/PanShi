<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NCard, NGrid, NGridItem, NSpace, NStatistic, NTag, NAlert } from 'naive-ui'
import { getDashboardStats, type DashboardStatsDto } from '@/api/dashboard'
import { useNoticeStore } from '@/stores/notice'
import { usePermissionStore } from '@/stores/permission'
import { useUserStore } from '@/stores/user'
import { formatDate } from '@/utils/format'
import AppIcon from '@/components/AppIcon.vue'

/** 首页欢迎卡片（占位）：真实工作台在后续批次按菜单授权展开 */
const router = useRouter()
const user = useUserStore()
const perm = usePermissionStore()
const notice = useNoticeStore()
const stats = ref<DashboardStatsDto>({})

const tiles = computed(() => [
  { key: 'todo', label: '我的待办', value: stats.value.todoCount ?? notice.todoCount, icon: 'lucide:inbox', color: '#2563eb' },
  { key: 'unread', label: '未读消息', value: stats.value.unreadCount ?? notice.unreadCount, icon: 'lucide:mail', color: '#0ea5e9' },
  { key: 'notice', label: '在办公告', value: stats.value.noticeCount ?? 0, icon: 'lucide:megaphone', color: '#d97706' },
  { key: 'menu', label: '可访问菜单', value: perm.accessRoutes.length, icon: 'lucide:list-tree', color: '#16a34a' }
])

const greeting = computed(() => {
  const hour = new Date().getHours()
  if (hour < 6) return '凌晨好'
  if (hour < 12) return '上午好'
  if (hour < 14) return '中午好'
  if (hour < 18) return '下午好'
  return '晚上好'
})

onMounted(() => {
  void getDashboardStats().then((data) => (stats.value = data ?? {})).catch(() => undefined)
  void notice.refreshCount()
})
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false" class="ps-hero">
      <NSpace align="center" :size="16">
        <div class="ps-hero__avatar"><AppIcon name="lucide:layout-dashboard" :size="30" /></div>
        <div>
          <h2 class="ps-hero__title">{{ greeting }}，{{ user.displayName }}</h2>
          <p class="ps-hero__sub">
            欢迎使用 磐石管理底座 · 今天 {{ formatDate(new Date()) }} ·
            {{ user.profile?.deptName || '未分配部门' }}
          </p>
        </div>
        <NTag v-if="user.isAdmin" type="error" :bordered="false" size="small">内置管理员</NTag>
      </NSpace>
      <p class="ps-hero__desc">
        左侧菜单由 <code>/api/v1/sys/menu/tree/my</code> 动态生成，页签/权限码/实时通知均已打通；
        系统管理与审批流页面将在批次 #12 接入。
      </p>
    </NCard>

    <NGrid responsive="screen" cols="4 s:2" :x-gap="14" :y-gap="14" class="ps-hero__grid">
      <NGridItem v-for="tile in tiles" :key="tile.key">
        <NCard :bordered="false" class="ps-tile">
          <NSpace align="center" :size="14">
            <div class="ps-tile__icon" :style="{ background: `${tile.color}1f`, color: tile.color }">
              <AppIcon :name="tile.icon" :size="20" />
            </div>
            <NStatistic :label="tile.label" :value="tile.value" />
          </NSpace>
        </NCard>
      </NGridItem>
    </NGrid>

    <NAlert type="info" :bordered="false" class="ps-hero__alert" title="骨架已就绪">
      登录 / 令牌单飞刷新 / 动态路由 / 多页签 keep-alive / 亮暗主题 / SignalR 通知 已可用。
      <NSpace :size="8" style="margin-top: 10px">
        <NButton size="small" @click="router.push('/profile')">前往个人中心</NButton>
        <NButton size="small" tertiary @click="router.back()">返回上一页</NButton>
      </NSpace>
    </NAlert>
  </div>
</template>

<style scoped>
.ps-hero__avatar {
  display: grid;
  place-items: center;
  width: 54px;
  height: 54px;
  border-radius: 14px;
  background: rgba(37, 99, 235, 0.12);
  color: var(--ps-primary);
  flex: none;
}

.ps-hero__title {
  margin: 0;
  font-size: 21px;
  font-weight: 650;
}

.ps-hero__sub {
  margin: 6px 0 0;
  font-size: 13px;
  color: var(--ps-text-3);
}

.ps-hero__desc {
  margin: 18px 0 0;
  font-size: 13px;
  line-height: 1.8;
  color: var(--ps-text-3);
}

.ps-hero__grid {
  margin-top: 14px;
}

.ps-tile {
  border-radius: 12px;
}

.ps-tile__icon {
  display: grid;
  place-items: center;
  width: 42px;
  height: 42px;
  border-radius: 11px;
  flex: none;
}

.ps-hero__alert {
  margin-top: 14px;
  border-radius: 12px;
}
</style>
