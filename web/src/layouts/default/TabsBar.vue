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

function closeTab(raw: string | number): void {
  const next = tabs.close(String(raw))
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
      @close="closeTab"
    >
      <NTabPane
        v-for="item in items"
        :key="item.path"
        :name="item.path"
        :closable="item.closable"
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

/* Chrome 标签风：默认透明、悬停浅灰、活动白底(暗色深底)+顶部主色指示条+圆角，压在分隔线上与内容相连。
   ⚠️ Naive card 型用运行时 CSS-in-JS 注入(特异性高、加载更晚)，故关键属性必须 !important 才盖得住。 */
.ps-tabs :deep(.n-tabs-tab) {
  position: relative;
  height: 34px;
  margin: 0 4px -1px 0;
  padding: 0 12px !important;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  border: 1px solid transparent !important;
  border-bottom: none !important;
  border-radius: 10px 10px 0 0 !important;
  background: transparent !important;
  color: var(--ps-text-3) !important;
  font-weight: 500;
  transition: background 0.15s ease, color 0.15s ease;
}

.ps-tabs :deep(.n-tabs-tab:hover) {
  background: var(--ps-tab-hover) !important;
  color: var(--ps-text-1) !important;
}

.ps-tabs :deep(.n-tabs-tab--active),
.ps-tabs :deep(.n-tabs-tab--active:hover) {
  background: var(--ps-tab-active) !important;
  border-color: var(--ps-card-border) !important;
  color: var(--ps-primary) !important;
  font-weight: 600;
}

.ps-tabs :deep(.n-tabs-tab--active::before) {
  content: "";
  position: absolute;
  left: 10px;
  right: 10px;
  top: 0;
  height: 2px;
  border-radius: 0 0 3px 3px;
  background: var(--ps-primary);
}

.ps-tabs :deep(.n-tabs-tab__close) {
  margin-left: 2px;
}

.ps-tabs__more {
  flex: none;
  margin-bottom: 5px;
}

.ps-tabs__label {
  display: inline-block;
  max-width: 12em;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
