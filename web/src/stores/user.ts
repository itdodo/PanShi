import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { changePassword, fetchFileBlobUrl, getProfile, login as loginApi, logout as logoutApi, updateProfile } from '@/api/auth'
import type { ChangePasswordDto, LoginDto, LoginResultDto, ProfileDto, UpdateProfileDto } from '@/api/auth'
import { tokenStore } from '@/utils/token'

/**
 * 用户态：token/refreshToken 持久 localStorage（真源在 utils/token，避免 http ↔ store 循环依赖）。
 * 401 单飞刷新成功后 http 层广播 `ps:token-refreshed`，本 store 监听并同步 ref。
 */
export const useUserStore = defineStore('user', () => {
  const token = ref(tokenStore.access)
  const refreshToken = ref(tokenStore.refresh)
  const profile = ref<ProfileDto | null>(null)
  const roles = ref<string[]>([])
  const permissions = ref<string[]>([])
  const isAdmin = ref(false)
  /** profile 是否已加载（守卫据此决定要不要拉 profile + 菜单树） */
  const loaded = ref(false)
  const mustChangePassword = ref(false)

  const logged = computed(() => !!token.value)
  const userId = computed(() => profile.value?.id ?? '')
  const userName = computed(() => profile.value?.userName ?? '')
  const displayName = computed(() => profile.value?.nickName || profile.value?.userName || '未知用户')

  function syncFromStorage(): void {
    token.value = tokenStore.access
    refreshToken.value = tokenStore.refresh
    // 长开页签：刷新令牌时后端可能回传 mustChangePassword，同步给守卫拦截
    if (loaded.value) mustChangePassword.value = tokenStore.mustChange
  }
  window.addEventListener('ps:token-refreshed', syncFromStorage)

  /**
   * 头像 objectURL（由 profile.avatarFileId 拉字节流生成）。
   * 顶栏与个人中心直接绑 NAvatar 的 src；空串=没头像，退回显示首字母。
   */
  const avatarUrl = ref('')
  let avatarLoadedId = ''

  /** 幂等：id 没变就不重拉；失败清掉已记 id 以便下次重试 */
  async function syncAvatar(): Promise<void> {
    const id = profile.value?.avatarFileId ?? ''
    if (id === avatarLoadedId) return
    avatarLoadedId = id
    const prev = avatarUrl.value
    avatarUrl.value = ''
    if (prev) URL.revokeObjectURL(prev)
    if (!id) return
    try {
      avatarUrl.value = await fetchFileBlobUrl(id)
    } catch {
      avatarLoadedId = ''
    }
  }

  function applyProfile(p: ProfileDto): void {
    profile.value = p
    roles.value = p.roles ?? []
    permissions.value = p.permissions ?? []
    isAdmin.value = !!p.isAdmin
    loaded.value = true
    void syncAvatar()
  }

  /** 清空登录态与派生路由/页签（动态 import 规避 store 间循环引用） */
  function resetAll(): void {
    token.value = ''
    refreshToken.value = ''
    profile.value = null
    roles.value = []
    permissions.value = []
    isAdmin.value = false
    loaded.value = false
    mustChangePassword.value = false
    if (avatarUrl.value) URL.revokeObjectURL(avatarUrl.value)
    avatarUrl.value = ''
    avatarLoadedId = ''
    tokenStore.clear()
    void Promise.all([import('./permission'), import('./tabs')]).then(([perm, tabs]) => {
      perm.usePermissionStore().reset()
      tabs.useTabsStore().reset()
    })
  }

  async function login(dto: LoginDto): Promise<LoginResultDto> {
    const result = await loginApi(dto)
    token.value = result.token
    refreshToken.value = result.refreshToken
    mustChangePassword.value = !!result.mustChangePassword
    tokenStore.set(result.token, result.refreshToken, mustChangePassword.value)
    loaded.value = false
    return result
  }

  async function loadProfile(force = false): Promise<ProfileDto> {
    if (profile.value && loaded.value && !force) return profile.value
    const p = await getProfile()
    applyProfile(p)
    return p
  }

  async function saveProfile(dto: UpdateProfileDto): Promise<void> {
    await updateProfile(dto)
    await loadProfile(true)
  }

  async function changePwd(dto: ChangePasswordDto): Promise<void> {
    await changePassword(dto)
    mustChangePassword.value = false
  }

  /** 退出：接口失败也要清本地（服务端会话有 TTL 兜底） */
  async function logout(callApi = true): Promise<void> {
    if (callApi) {
      try {
        await logoutApi()
      } catch (err) {
        console.warn('[user] 退出接口调用失败，仍清理本地登录态', err)
      }
    }
    resetAll()
  }

  /** 权限码判定：admin 的 permissions 已含全量，无需特判 */
  function hasPermission(code?: string | string[] | null): boolean {
    if (!code || (Array.isArray(code) && code.length === 0)) return true
    const list = permissions.value
    if (list.includes('*:*:*') || list.includes('*')) return true
    const codes = Array.isArray(code) ? code : [code]
    return codes.some((c) => list.includes(c))
  }

  function hasRole(code?: string | string[] | null): boolean {
    if (!code) return true
    const codes = Array.isArray(code) ? code : [code]
    return codes.some((c) => roles.value.includes(c))
  }

  return {
    token,
    refreshToken,
    profile,
    roles,
    permissions,
    isAdmin,
    loaded,
    mustChangePassword,
    logged,
    userId,
    userName,
    displayName,
    avatarUrl,
    login,
    logout,
    loadProfile,
    saveProfile,
    syncAvatar,
    changePwd,
    applyProfile,
    resetAll,
    syncFromStorage,
    hasPermission,
    hasRole
  }
})
