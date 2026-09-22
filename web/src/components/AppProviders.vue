<script setup lang="ts">
import { computed } from 'vue'
import {
  dateZhCN,
  darkTheme,
  NConfigProvider,
  NDialogProvider,
  NLoadingBarProvider,
  NMessageProvider,
  NNotificationProvider,
  zhCN,
  type GlobalTheme,
  type GlobalThemeOverrides
} from 'naive-ui'
import { darkThemeOverrides, lightThemeOverrides } from '@/styles/theme'
import { isDark } from '@/utils/themeState'

/**
 * 全局 provider：主题（亮暗可切）+ Message/Dialog/Notification/LoadingBar。
 * 放在 App.vue 而非布局内，登录页 / 403 / 404 同样能弹提示。
 * ⚠️ setup 之外（http 拦截器、守卫、SignalR 回调）用 utils/feedback 的 createDiscreteApi。
 */
const theme = computed<GlobalTheme | null>(() => (isDark.value ? darkTheme : null))
const themeOverrides = computed<GlobalThemeOverrides>(() =>
  isDark.value ? darkThemeOverrides : lightThemeOverrides
)
</script>

<template>
  <NConfigProvider :theme="theme" :theme-overrides="themeOverrides" :locale="zhCN" :date-locale="dateZhCN">
    <NLoadingBarProvider>
      <NMessageProvider :duration="3200">
        <NDialogProvider :mask-closable="false">
          <NNotificationProvider placement="bottom-right" :duration="5000">
            <slot />
          </NNotificationProvider>
        </NDialogProvider>
      </NMessageProvider>
    </NLoadingBarProvider>
  </NConfigProvider>
</template>
