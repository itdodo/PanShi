<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NResult, NSpace } from 'naive-ui'
import { usePermissionStore } from '@/stores/permission'
import { useUserStore } from '@/stores/user'

/** 无权限友好页：无角色/菜单为空的兜底，绝不白屏（蓝图 §六） */
const route = useRoute()
const router = useRouter()
const user = useUserStore()
const perm = usePermissionStore()

const reason = computed(() =>
  typeof route.query.reason === 'string' && route.query.reason
    ? route.query.reason
    : '当前账号没有可访问的菜单，请联系管理员分配角色'
)

function goHome(): void {
  void router.replace(perm.loaded && perm.hasAnyMenu ? perm.homePath : '/')
}
</script>

<template>
  <div class="ps-result">
    <NResult status="403" title="无访问权限" :description="reason" size="large">
      <template #footer>
        <NSpace justify="center">
          <NButton v-if="user.logged" type="primary" @click="goHome">返回首页</NButton>
          <NButton @click="router.replace('/login')">重新登录</NButton>
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
