<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NButton, NInput, NPopover, NSpace } from 'naive-ui'
import AppIcon from './AppIcon.vue'
import { listIconNames, normalizeIconName } from '@/utils/menuIcon'

/**
 * 图标选择器：搜索 + 网格点选，同时保留手输框（便于粘贴已知名字）。
 * 候选集来自 utils/menuIcon 装载的 lucide 离线集合，即「能选的一定能渲染」，
 * 不会选出个 AppIcon 认不出的名字然后静默变兜底图标。
 */
const props = withDefaults(
  defineProps<{
    value?: string | null
    placeholder?: string
    /** 一次最多渲染的格子数（lucide 近两千个，全渲染会拖慢弹层） */
    maxVisible?: number
    inputWidth?: number
  }>(),
  { value: '', placeholder: '如 lucide:users', maxVisible: 160, inputWidth: 190 }
)

const emit = defineEmits<{ 'update:value': [string] }>()

const open = ref(false)
const keyword = ref('')

// 弹层内容在关闭后仍挂载，搜索词会跨次保留：不重置的话重开时列表里没有当前图标，
// 看起来像「没选中」
watch(open, (v) => { if (v) keyword.value = '' })

const current = computed(() => normalizeIconName(props.value))

const matches = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  const all = listIconNames()
  const hit = kw ? all.filter((name) => name.includes(kw)) : all
  const shown = hit.slice(0, props.maxVisible)
  // 只渲染前 N 个时，当前值往往在 N 之后 → 高亮格子看不见，像是没选。把它顶到第一位
  if (current.value && !shown.includes(current.value)) shown.unshift(current.value)
  return { shown, total: hit.length }
})

function pick(name: string): void {
  emit('update:value', name)
  open.value = false
}

function clear(): void {
  emit('update:value', '')
  keyword.value = ''
}

function onInput(raw: string): void {
  emit('update:value', raw.trim())
}
</script>

<template>
  <NSpace align="center" :size="8">
    <NPopover v-model:show="open" trigger="click" placement="bottom-start" :width="396" :show-arrow="false">
      <template #trigger>
        <NButton size="small" class="ps-icon-picker__trigger">
          <template #icon><AppIcon :name="current || 'lucide:image'" :size="16" /></template>
          选择图标
        </NButton>
      </template>

      <div class="ps-icon-picker">
        <NInput v-model:value="keyword" size="small" clearable placeholder="搜索图标名，如 user / arrow / file">
          <template #prefix><AppIcon name="lucide:search" :size="14" /></template>
        </NInput>

        <div class="ps-icon-picker__grid">
          <!-- 用原生 title 而不是 NTooltip：一百多个格子挂弹层组件会明显拖慢开合 -->
          <button
            v-for="name in matches.shown"
            :key="name"
            type="button"
            :title="name"
            class="ps-icon-picker__cell"
            :class="{ 'ps-icon-picker__cell--active': name === current }"
            @click="pick(name)"
          >
            <AppIcon :name="name" :size="18" />
          </button>
        </div>

        <div class="ps-icon-picker__foot">
          <span class="ps-muted">
            共 {{ matches.total }} 个可选<span v-if="matches.total > matches.shown.length">，已显示前 {{ matches.shown.length }} 个，继续输入可缩小范围</span>
          </span>
          <NButton size="tiny" text @click="clear">清空</NButton>
        </div>
      </div>
    </NPopover>

    <NInput
      :value="props.value ?? ''"
      size="small"
      :style="{ width: `${inputWidth}px` }"
      :placeholder="placeholder"
      maxlength="128"
      clearable
      @update:value="onInput"
    />
    <AppIcon :name="props.value" :size="20" />
  </NSpace>
</template>

<style scoped>
.ps-icon-picker {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.ps-icon-picker__grid {
  display: grid;
  grid-template-columns: repeat(10, 1fr);
  gap: 4px;
  max-height: 232px;
  overflow-y: auto;
}

.ps-icon-picker__cell {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border: 1px solid transparent;
  border-radius: 8px;
  background: transparent;
  color: var(--ps-text-2, #4b5563);
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s, color 0.15s;
}

.ps-icon-picker__cell:hover {
  color: var(--ps-primary);
  border-color: var(--ps-card-border);
  background: var(--ps-tab-hover);
}

.ps-icon-picker__cell--active {
  color: var(--ps-primary);
  border-color: var(--ps-primary);
  background: var(--ps-primary-soft);
}

.ps-icon-picker__foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 12px;
}
</style>
