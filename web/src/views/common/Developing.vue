<script setup lang="ts">
import { NButton, NResult, NSpace } from 'naive-ui'
import { useRoute } from 'vue-router'

/**
 * 菜单 component 指向的文件尚未实现时的占位页（批次 #12 会逐页替换）。
 * 保证「后端菜单先于前端页面」也不会白屏报错。
 */
const route = useRoute()
</script>

<template>
  <div class="ps-result">
    <NResult
      status="info"
      title="页面建设中"
      size="large"
      :description="`菜单已授权，但组件尚未实现：${String(route.meta.title ?? route.path)}`"
    >
      <template #footer>
        <NSpace justify="center">
          <NTag :bordered="false" size="small">{{ route.meta.menuId ? `菜单 Id：${route.meta.menuId}` : '' }}</NTag>
          <NTag :bordered="false" size="small">路径：{{ route.path }}</NTag>
        </NSpace>
      </template>
    </NResult>
  </div>
</template>

<style scoped>
.ps-result {
  display: grid;
  place-items: center;
  min-height: 60vh;
  padding: 24px;
}
</style>
