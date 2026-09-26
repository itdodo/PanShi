<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NAvatar, NButton, NPopover, NTag } from 'naive-ui'
import AppIcon from '@/components/AppIcon.vue'
import { dialog } from '@/utils/feedback'
import { useUserStore } from '@/stores/user'
import { useRealtime } from '@/composables/useRealtime'
import { resetKickFlag } from '@/utils/session'

/** 用户区：悬浮弹出「个人信息卡 + 操作菜单」（单弹层，⚠️ 不再 Dropdown+Popover 双层叠加） */
const router = useRouter()
const user = useUserStore()
const { disconnect } = useRealtime()
const show = ref(false)

const actions = [
  { key: 'profile', label: '个人中心', icon: 'lucide:user-round' },
  { key: 'password', label: '修改密码', icon: 'lucide:key-round' },
  { key: 'logout', label: '退出登录', icon: 'lucide:log-out', danger: true }
] as const

const initial = computed(() => (user.displayName || user.userName || '?').slice(0, 1).toUpperCase())
const roleText = computed(() => (user.roles.length ? user.roles.join(' / ') : '未分配角色'))

function onAction(key: (typeof actions)[number]['key']): void {
  show.value = false
  if (key === 'profile') {
    void router.push('/profile')
    return
  }
  if (key === 'password') {
    void router.push({ path: '/profile', query: { tab: 'password' } })
    return
  }
  if (key === 'logout') confirmLogout()
}

function confirmLogout(): void {
  dialog.warning({
    title: '退出登录',
    content: '确认退出当前账号？未保存的内容将丢失。',
    positiveText: '退出',
    negativeText: '取消',
    onPositiveClick: async () => {
      await disconnect()
      await user.logout()
      resetKickFlag()
      await router.replace('/login')
    }
  })
}
</script>

<template>
  <NPopover
    v-model:show="show"
    trigger="hover"
    placement="bottom-end"
    :width="236"
    :content-style="{ padding: '0' }"
  >
    <template #trigger>
      <NButton text class="ps-user">
        <!-- ⚠️ NAvatar 的默认插槽与 src 互斥（源码：有 slot children 就只渲染文字、永不渲染 img），
             所以有头像/无头像必须两支分开渲染，不能写成「src + 插槽兜底」。 -->
        <NAvatar v-if="user.avatarUrl" round :size="30" :src="user.avatarUrl" class="ps-user__avatar" />
        <NAvatar v-else round :size="30" class="ps-user__avatar">{{ initial }}</NAvatar>
        <span class="ps-user__name ps-ellipsis">{{ user.displayName }}</span>
      </NButton>
    </template>

    <div class="ps-user__panel">
      <div class="ps-user__card">
        <div class="ps-user__row">
          <b>{{ user.displayName }}</b>
          <span class="ps-muted">@{{ user.userName }}</span>
        </div>
        <div class="ps-user__row ps-muted">{{ user.profile?.deptName || '未分配部门' }}</div>
        <div class="ps-user__row">
          <NTag v-if="user.isAdmin" size="tiny" type="error" :bordered="false">内置管理员</NTag>
          <span class="ps-muted">{{ roleText }}</span>
        </div>
      </div>

      <ul class="ps-user__menu">
        <li v-for="item in actions" :key="item.key">
          <button
            type="button"
            class="ps-user__item"
            :class="{ 'ps-user__item--danger': 'danger' in item && item.danger }"
            @click="onAction(item.key)"
          >
            <AppIcon :name="item.icon" :size="16" />
            <span>{{ item.label }}</span>
          </button>
        </li>
      </ul>
    </div>
  </NPopover>
</template>

<style scoped>
.ps-user {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  max-width: 190px;
  padding: 0 4px;
}

.ps-user__avatar {
  background: var(--ps-primary);
  color: #fff;
  flex: none;
}

.ps-user__name {
  font-size: 13px;
}

.ps-user__panel {
  margin: -10px -14px;
}

.ps-user__card {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 14px;
  font-size: 13px;
}

.ps-user__row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.ps-user__menu {
  list-style: none;
  margin: 0;
  padding: 4px;
  border-top: 1px solid rgba(128, 128, 128, 0.16);
}

.ps-user__item {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 7px 10px;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: inherit;
  font-size: 13px;
  text-align: left;
  cursor: pointer;
}

.ps-user__item:hover {
  background: var(--ps-primary-soft);
}

.ps-user__item--danger {
  color: #d03050;
}

.ps-user__item--danger:hover {
  background: rgba(208, 48, 80, 0.1);
}
</style>
