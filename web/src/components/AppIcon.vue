<script setup lang="ts">
import { computed } from 'vue'
import { Icon } from '@iconify/vue/offline'
import { ensureIconCollection, resolveMenuIcon } from '@/utils/menuIcon'

/**
 * 图标组件：入参是后端存的 iconify 字符串（`lucide:users`），
 * 内部经 utils/menuIcon 解析，未知图标自动兜底文档图标；离线版不打 Iconify API。
 */
const props = withDefaults(
  defineProps<{
    name?: string | null
    size?: number | string
    color?: string
  }>(),
  { name: '', size: 18, color: undefined }
)

void ensureIconCollection()

const icon = computed(() => resolveMenuIcon(props.name))
</script>

<template>
  <Icon :icon="icon" :width="props.size" :height="props.size" :style="props.color ? { color: props.color } : undefined" />
</template>
