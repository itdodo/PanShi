<script setup lang="ts">
import { onMounted } from 'vue'
import AppProviders from './components/AppProviders.vue'
import { ensureIconCollection } from '@/utils/menuIcon'

onMounted(() => {
  // 预热 lucide 图标集合（菜单 icon 运行时解析，失败自动兜底 Document）
  void ensureIconCollection()
})
</script>

<template>
  <AppProviders>
    <RouterView v-slot="{ Component }">
      <component :is="Component" />
    </RouterView>
  </AppProviders>
</template>
