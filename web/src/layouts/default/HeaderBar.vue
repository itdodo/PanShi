<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { NBreadcrumb, NBreadcrumbItem, NButton, NTooltip } from 'naive-ui'
import { usePermissionStore } from '@/stores/permission'
import { useTabsStore } from '@/stores/tabs'
import { isDark, toggleDark } from '@/utils/themeState'
import NoticeBell from './NoticeBell.vue'
import UserActions from './UserActions.vue'

const props = defineProps<{ collapsed?: boolean }>()
const emit = defineEmits<{ (e: 'toggle-collapse'): void }>()

const route = useRoute()
const perm = usePermissionStore()
const tabs = useTabsStore()

/** 面包屑：菜单链（目录 → 菜单），静态页退化为 meta.title */
const trail = computed<string[]>(() => {
  const nodes = perm.findMenuTrail(route.path)
  const labels = nodes.map((n) => n.menuName)
  if (labels.length) return labels
  return route.meta.title ? [route.meta.title] : []
})

function refreshCurrent(): void {
  tabs.refresh(route.path)
}
</script>

<template>
  <div class="ps-header">
    <div class="ps-header__left">
      <NTooltip trigger="hover" :delay="400">
        <template #trigger>
          <NButton quaternary circle size="small" :aria-label="props.collapsed ? '展开菜单' : '折叠菜单'" @click="emit('toggle-collapse')">
            <icon-lucide-panel-left v-if="!props.collapsed" />
            <icon-lucide-panel-left-close v-else />
          </NButton>
        </template>
        {{ props.collapsed ? '展开菜单' : '折叠菜单' }}
      </NTooltip>

      <NBreadcrumb class="ps-header__crumb">
        <NBreadcrumbItem v-for="(label, index) in trail" :key="`${label}-${index}`">{{ label }}</NBreadcrumbItem>
      </NBreadcrumb>
    </div>

    <div class="ps-header__right">
      <NTooltip trigger="hover" :delay="400">
        <template #trigger>
          <NButton quaternary circle size="small" aria-label="刷新当前页签" @click="refreshCurrent">
            <icon-lucide-refresh-ccw />
          </NButton>
        </template>
        刷新当前页签
      </NTooltip>

      <NTooltip trigger="hover" :delay="400">
        <template #trigger>
          <NButton quaternary circle size="small" :aria-label="isDark ? '切换亮色' : '切换暗色'" @click="toggleDark()">
            <icon-lucide-sun v-if="isDark" />
            <icon-lucide-moon v-else />
          </NButton>
        </template>
        {{ isDark ? '亮色模式' : '暗色模式' }}
      </NTooltip>

      <NoticeBell />
      <UserActions />
    </div>
  </div>
</template>

<style scoped>
.ps-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 100%;
  padding: 0 12px 0 8px;
  gap: 12px;
}

.ps-header__left,
.ps-header__right {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.ps-header__crumb {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.ps-header :deep(svg) {
  width: 17px;
  height: 17px;
  display: block;
}
</style>
