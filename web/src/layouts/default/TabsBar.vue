<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NDropdown, NTabPane, NTabs, type DropdownOption } from 'naive-ui'
import { useTabsStore } from '@/stores/tabs'

/** 多页签条：点击切换 / 关闭 / 刷新当前 / 关闭其他 / 关闭全部（sessionStorage 持久化在 store 内） */
const route = useRoute()
const router = useRouter()
const tabs = useTabsStore()

const items = computed(() => tabs.tabs)

const actions = computed<DropdownOption[]>(() => [
  { key: 'refresh', label: '刷新当前页签' },
  { key: 'others', label: '关闭其他页签' },
  { key: 'all', label: '关闭全部页签' }
])

function activate(raw: string | number): void {
  const path = String(raw)
  const hit = tabs.tabs.find((t) => t.path === path)
  if (!hit) return
  tabs.touch(path)
  if (hit.fullPath !== route.fullPath) void router.push(hit.fullPath)
}

function closeTab(path: string): void {
  const next = tabs.close(path)
  if (next) void router.push(next)
}

function onAction(key: string | number): void {
  const code = String(key)
  if (code === 'refresh') {
    tabs.refresh(route.path)
    return
  }
  if (code === 'others') {
    const next = tabs.closeOthers(route.path)
    if (next) void router.push(next)
    return
  }
  if (code === 'all') void router.push(tabs.closeAll())
}
</script>

<template>
  <div class="ps-tabs">
    <NTabs
      :value="tabs.activePath"
      type="card"
      size="small"
      :animated="false"
      class="ps-tabs__strip"
      @update:value="activate"
    >
      <NTabPane
        v-for="item in items"
        :key="item.path"
        :name="item.path"
        :closable="item.closable"
        @close="closeTab(item.path)"
      >
        <template #tab>
          <span class="ps-tabs__label">{{ item.title }}</span>
        </template>
      </NTabPane>
    </NTabs>

    <NDropdown trigger="click" placement="bottom-end" :options="actions" @select="onAction">
      <NButton class="ps-tabs__more" size="tiny" quaternary aria-label="页签操作">
        <icon-lucide-ellipsis />
      </NButton>
    </NDropdown>
  </div>
</template>

<style scoped>
.ps-tabs {
  display: flex;
  align-items: flex-end;
  gap: 4px;
  height: 100%;
  padding: 0 8px 0 6px;
}

.ps-tabs__strip {
  flex: 1 1 auto;
  min-width: 0;
}

/* ⚠️ 页签仅作导航条用（真实页面内容由布局 RouterView 渲染），隐藏 NTabPane 的空内容面板，
   否则每个空面板占 ~8px，会在页签与内容之间形成「两条线夹一条空白带」。 */
.ps-tabs :deep(.n-tabs-pane-wrapper),
.ps-tabs :deep(.n-tab-pane) {
  display: none;
}

.ps-tabs :deep(.n-tabs-nav) {
  border-bottom: none;
}

.ps-tabs :deep(.n-tabs-tab) {
  border-radius: 7px 7px 0 0;
}

.ps-tabs :deep(.n-tabs-tab + .n-tabs-tab) {
  margin-left: 4px;
}

.ps-tabs__more {
  flex: none;
  margin-bottom: 4px;
}

.ps-tabs__label {
  display: inline-block;
  max-width: 12em;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  vertical-align: bottom;
}
</style>
