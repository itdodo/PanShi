import { computed } from 'vue'
import { createDiscreteApi, darkTheme, dateZhCN, zhCN, type ConfigProviderProps } from 'naive-ui'
import { darkThemeOverrides, lightThemeOverrides } from '@/styles/theme'
import { isDark } from './themeState'

/**
 * setup 之外可用的 Naive UI 反馈 API（踩坑红线：全局 message 在 setup 外必须用 createDiscreteApi）。
 * http 拦截器、SignalR 回调、路由守卫等模块级代码统一从这里取，跟随亮暗主题。
 */
const api = createDiscreteApi(['message', 'notification', 'dialog'], {
  configProviderProps: computed<ConfigProviderProps>(() => ({
    theme: isDark.value ? darkTheme : undefined,
    locale: zhCN,
    dateLocale: dateZhCN,
    themeOverrides: isDark.value ? darkThemeOverrides : lightThemeOverrides
  }))
})

export const message = api.message
export const notification = api.notification
export const dialog = api.dialog
export const naiveLocale = { locale: zhCN, dateLocale: dateZhCN }
