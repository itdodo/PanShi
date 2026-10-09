<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { darkTheme, NConfigProvider, NMenu, type GlobalThemeOverrides, type MenuOption } from 'naive-ui'
import { MENU_TYPE, type MenuTreeNode } from '@/api/menu'
import { usePermissionStore } from '@/stores/permission'

/**
 * 左侧深色菜单：递归渲染后端菜单树（store 里递归转成 NMenu options）。
 * 叶子 key = 路由 path，目录 key = menu:id；图标经 utils/menuIcon 解析，未知图标兜底 Document。
 * 用内层 NConfigProvider(darkTheme) 固定深色，Menu 默认底色透明，故深色渐变可直接透出。
 */
const props = withDefaults(defineProps<{ collapsed?: boolean; width?: number }>(), {
  collapsed: false,
  width: 224
})

const route = useRoute()
const router = useRouter()
const perm = usePermissionStore()

const siderOverrides: GlobalThemeOverrides = {
  common: {
    primaryColor: '#3d7bf4',
    primaryColorHover: '#5b93f7',
    primaryColorPressed: '#2563eb',
    primaryColorSuppl: '#5b93f7',
    borderRadius: '8px'
  }
}

const options = computed<MenuOption[]>(() => perm.menuOptions)
const active = computed(() => route.path)
const siderWidth = computed(() => (props.collapsed ? 64 : props.width))
const expandedKeys = ref<string[]>([])

/** 定位当前页的祖先目录 key，自动展开 */
function dirKeysOf(nodes: MenuTreeNode[], path: string, trail: string[] = []): string[] | null {
  for (const node of nodes) {
    if (node.menuType === MENU_TYPE.Button || node.visible === false) continue
    const nodePath = (node.path ?? '').trim()
    if (node.menuType === MENU_TYPE.Menu && nodePath && nodePath === path) return trail
    if (node.children?.length) {
      const next = node.menuType === MENU_TYPE.Directory ? [...trail, `menu:${node.id}`] : trail
      const hit = dirKeysOf(node.children, path, next)
      if (hit) return hit
    }
  }
  return null
}

watch(
  () => route.path,
  (path) => {
    const keys = dirKeysOf(perm.rawMenus, path)
    if (keys?.length) expandedKeys.value = [...new Set([...expandedKeys.value, ...keys])]
  },
  { immediate: true }
)

function onSelect(key: string | number): void {
  const path = String(key)
  if (!path.startsWith('/')) return
  if (path !== route.path) void router.push(path)
}

function onExpand(keys: Array<string | number>): void {
  expandedKeys.value = keys.map(String)
}
</script>

<template>
  <NConfigProvider :theme="darkTheme" :theme-overrides="siderOverrides">
    <div class="ps-side" :class="{ 'ps-side--collapsed': props.collapsed }" :style="{ width: `${siderWidth}px` }">
      <div class="ps-side__brand">
        <span class="ps-side__logo"><icon-lucide-hexagon /></span>
        <span v-show="!props.collapsed" class="ps-side__title ps-ellipsis">磐石管理系统</span>
      </div>
      <div class="ps-side__menu">
        <NMenu
          :value="active"
          :options="options"
          :collapsed="props.collapsed"
          :collapsed-width="64"
          :collapsed-icon-size="19"
          :expanded-keys="expandedKeys"
          :indent="18"
          accordion
          @update:value="onSelect"
          @update:expanded-keys="onExpand"
        />
      </div>
      <div v-show="!props.collapsed" class="ps-side__foot">
        <span>Vue3 · Naive UI · Vite</span>
      </div>
    </div>
  </NConfigProvider>
</template>

<style scoped>
.ps-side {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: linear-gradient(180deg, #111827 0%, #0f172a 100%);
  transition: width 0.22s ease;
  overflow: hidden;
}

.ps-side__brand {
  display: flex;
  align-items: center;
  gap: 10px;
  height: var(--ps-header-h);
  padding: 0 14px;
  border-bottom: 1px solid rgba(148, 163, 184, 0.14);
  flex: none;
}

.ps-side__logo {
  display: grid;
  place-items: center;
  width: 30px;
  height: 30px;
  border-radius: 9px;
  background: rgba(37, 99, 235, 0.2);
  color: #93b4fb;
  flex: none;
}

.ps-side__logo :deep(svg) {
  width: 18px;
  height: 18px;
}

.ps-side__title {
  color: #e2e8f0;
  font-size: 15px;
  font-weight: 600;
  letter-spacing: 0.3px;
}

.ps-side__menu {
  flex: 1 1 auto;
  overflow-x: hidden;
  overflow-y: auto;
  padding: 8px 6px;
}

/* 收起态：一级菜单图标水平居中（Naive 折叠项默认有左偏，去掉容器左右内边距并强制居中） */
.ps-side--collapsed .ps-side__menu {
  padding-left: 0;
  padding-right: 0;
}

.ps-side--collapsed :deep(.n-menu-item-content) {
  justify-content: center;
  padding-left: 0;
  padding-right: 0;
}

.ps-side--collapsed :deep(.n-menu-item-content__icon) {
  margin-right: 0;
}

.ps-side__foot {
  flex: none;
  padding: 10px 16px;
  font-size: 12px;
  color: rgba(148, 163, 184, 0.6);
  border-top: 1px solid rgba(148, 163, 184, 0.14);
}
</style>
