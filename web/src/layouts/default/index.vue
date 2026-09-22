<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { NLayout } from 'naive-ui'
import HeaderBar from './HeaderBar.vue'
import SideMenu from './SideMenu.vue'
import TabsBar from './TabsBar.vue'
import { useNoticeStore } from '@/stores/notice'
import { useTabsStore } from '@/stores/tabs'
import { useRealtime } from '@/composables/useRealtime'

/**
 * 默认布局：深色可折叠侧栏 + 顶栏（面包屑/刷新/主题/铃铛/用户）+ 多页签 + keep-alive 内容区。
 * 主题与全局 provider 在 App.vue 的 AppProviders（登录页也需要）。
 */
const tabs = useTabsStore()
const collapsed = ref(typeof window !== 'undefined' && window.innerWidth < 992)
const cachedNames = computed<string[]>(() => tabs.cachedNames)
const { connect, disconnect } = useRealtime()

function onResize(): void {
  if (window.innerWidth < 992 && !collapsed.value) collapsed.value = true
}

onMounted(() => {
  window.addEventListener('resize', onResize)
  // SignalR（/hubs/notify）：连接失败静默降级，不影响页面
  void connect()
  void useNoticeStore().refreshCount()
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', onResize)
  void disconnect()
})
</script>

<template>
  <div class="ps-layout">
    <SideMenu :collapsed="collapsed" />

    <div class="ps-layout__main">
      <header class="ps-layout__header">
        <HeaderBar :collapsed="collapsed" @toggle-collapse="collapsed = !collapsed" />
      </header>

      <div class="ps-layout__tabs">
        <TabsBar />
      </div>

      <NLayout position="static" class="ps-layout__content" :content-style="{ padding: '0', background: 'var(--ps-page-bg)' }">
        <RouterView v-slot="{ Component, route: current }">
          <KeepAlive :include="cachedNames">
            <component :is="Component" :key="tabs.componentKey(current.path)" />
          </KeepAlive>
        </RouterView>
      </NLayout>
    </div>
  </div>
</template>

<style scoped>
.ps-layout {
  display: flex;
  height: 100%;
  overflow: hidden;
}

.ps-layout__main {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-width: 0;
}

.ps-layout__header {
  flex: none;
  height: var(--ps-header-h);
  border-bottom: 1px solid rgba(128, 128, 128, 0.14);
}

.ps-layout__tabs {
  flex: none;
  height: var(--ps-tabs-h);
  border-bottom: 1px solid rgba(128, 128, 128, 0.12);
}

.ps-layout__content {
  flex: 1 1 auto;
  overflow: auto;
}
</style>
