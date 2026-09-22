import { ref, watch } from 'vue'

const DARK_KEY = 'ps:dark'

function initial(): boolean {
  try {
    const saved = localStorage.getItem(DARK_KEY)
    if (saved === '1') return true
    if (saved === '0') return false
    return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false
  } catch {
    return false
  }
}

/** 亮暗开关（模块级单例：store / 组件 / createDiscreteApi 都能读，且不依赖 pinia 生命周期） */
export const isDark = ref<boolean>(initial())

function apply(dark: boolean): void {
  document.documentElement.classList.toggle('ps-dark', dark)
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
}

apply(isDark.value)

watch(isDark, (v) => {
  apply(v)
  try {
    localStorage.setItem(DARK_KEY, v ? '1' : '0')
  } catch {
    /* ignore */
  }
})

export function toggleDark(): void {
  isDark.value = !isDark.value
}

export function setDark(v: boolean): void {
  isDark.value = v
}
