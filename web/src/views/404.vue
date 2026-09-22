<script setup lang="ts">
import { useRouter } from 'vue-router'
import { NButton, NResult, NSpace } from 'naive-ui'
import { usePermissionStore } from '@/stores/permission'

/** 404：地址不在可访问路由表内 */
const router = useRouter()
const perm = usePermissionStore()

function goHome(): void {
  void router.replace(perm.loaded && perm.hasAnyMenu ? perm.homePath : '/')
}
</script>

<template>
  <div class="ps-result">
    <NResult status="404" title="页面不存在" description="地址无效，或该页面未在你的菜单授权范围内。" size="large">
      <template #footer>
        <NSpace justify="center">
          <NButton type="primary" @click="goHome">返回首页</NButton>
          <NButton @click="router.back()">返回上一页</NButton>
        </NSpace>
      </template>
    </NResult>
  </div>
</template>

<style scoped>
.ps-result {
  display: grid;
  place-items: center;
  height: 100%;
  padding: 24px;
}
</style>
