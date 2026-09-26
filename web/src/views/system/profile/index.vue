<script setup lang="ts">
import { computed, h, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import {
  NAlert,
  NAvatar,
  NButton,
  NCard,
  NDataTable,
  NDescriptions,
  NDescriptionsItem,
  NForm,
  NFormItem,
  NInput,
  NSpace,
  NTabPane,
  NTabs,
  NTag,
  NUpload,
  type DataTableColumns,
  type FormInst,
  type FormRules,
  type UploadCustomRequestOptions
} from 'naive-ui'
import { kickSession, listSessions, uploadAvatar, type SessionDto } from '@/api/auth'
import { useUserStore } from '@/stores/user'
import { dialog, message } from '@/utils/feedback'
import { formatDateTime, shortUserAgent } from '@/utils/format'

/** 个人中心：资料 / 修改密码 / 我的会话（全部对接真实 /auth 接口） */
type TabKey = 'info' | 'password' | 'sessions'

const route = useRoute()
const user = useUserStore()

const activeTab = ref<TabKey>('info')
const saving = ref(false)
const uploading = ref(false)
const infoFormRef = ref<FormInst | null>(null)
const pwdFormRef = ref<FormInst | null>(null)
const avatarFileId = ref<string | null>(null)

const infoModel = reactive({ nickName: '', phone: '', email: '' })
const pwdModel = reactive({ oldPassword: '', newPassword: '', confirmPassword: '' })

const infoRules: FormRules = {
  nickName: [{ required: true, message: '请输入昵称', trigger: ['input', 'blur'] }],
  phone: [
    {
      validator: (_rule: unknown, value: string) =>
        !value || /^1[3-9]\d{9}$/.test(value) ? true : new Error('手机号格式不正确（选填）'),
      trigger: ['input', 'blur']
    }
  ],
  email: [
    {
      validator: (_rule: unknown, value: string) =>
        !value || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value) ? true : new Error('邮箱格式不正确（选填）'),
      trigger: ['input', 'blur']
    }
  ]
}

const pwdRules: FormRules = {
  oldPassword: [{ required: true, message: '请输入原密码', trigger: ['input', 'blur'] }],
  newPassword: [
    { required: true, message: '请输入新密码', trigger: ['input', 'blur'] },
    {
      validator: (_rule: unknown, value: string) =>
        value && value.length >= 8 ? true : new Error('新密码至少 8 位'),
      trigger: ['input', 'blur']
    },
    {
      validator: (_rule: unknown, value: string) =>
        !value || value !== pwdModel.oldPassword ? true : new Error('新密码不能与原密码相同'),
      trigger: ['input', 'blur']
    }
  ],
  confirmPassword: [
    { required: true, message: '请再次输入新密码', trigger: ['input', 'blur'] },
    {
      validator: (_rule: unknown, value: string) =>
        value === pwdModel.newPassword ? true : new Error('两次输入的密码不一致'),
      trigger: ['input', 'blur']
    }
  ]
}

const permissions = computed(() => user.permissions.length)

function fillInfo(): void {
  infoModel.nickName = user.profile?.nickName ?? ''
  infoModel.phone = user.profile?.phone ?? ''
  infoModel.email = user.profile?.email ?? ''
  avatarFileId.value = null
}

/* ------------------------------- 我的会话 ------------------------------- */
const sessions = ref<SessionDto[]>([])
const sessionLoading = ref(false)

async function loadSessions(): Promise<void> {
  sessionLoading.value = true
  try {
    sessions.value = await listSessions()
  } catch {
    sessions.value = []
  } finally {
    sessionLoading.value = false
  }
}

function kick(row: SessionDto): void {
  dialog.warning({
    title: '下线该会话',
    content: `IP ${row.loginIp ?? '-'}（${shortUserAgent(row.userAgent)}）将被强制退出登录。`,
    positiveText: '下线',
    negativeText: '取消',
    onPositiveClick: async () => {
      await kickSession(row.id)
      message.success('已下线该会话')
      await loadSessions()
    }
  })
}

const sessionColumns: DataTableColumns<SessionDto> = [
  { title: '登录 IP', key: 'loginIp', width: 140, render: (row) => row.loginIp ?? '-' },
  { title: '设备', key: 'userAgent', ellipsis: { tooltip: true }, render: (row) => shortUserAgent(row.userAgent) },
  { title: '登录时间', key: 'createTime', width: 170, render: (row) => formatDateTime(row.createTime) },
  { title: '过期时间', key: 'expireTime', width: 170, render: (row) => formatDateTime(row.expireTime) },
  {
    title: '状态',
    key: 'current',
    width: 96,
    render: (row) => (row.current ? h(NTag, { size: 'small', type: 'success', bordered: false }, { default: () => '当前' }) : h(NTag, { size: 'small', bordered: false }, { default: () => '其它' }))
  },
  {
    title: '操作',
    key: 'actions',
    width: 90,
    render: (row) =>
      row.current
        ? h('span', { class: 'ps-muted' }, '—')
        : h(NButton, { size: 'tiny', type: 'error', tertiary: true, onClick: () => kick(row) }, { default: () => '下线' })
  }
]

/* --------------------------------- 动作 --------------------------------- */
async function saveInfo(): Promise<void> {
  const invalid = await infoFormRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return
  saving.value = true
  try {
    await user.saveProfile({
      nickName: infoModel.nickName.trim(),
      phone: infoModel.phone || null,
      email: infoModel.email || null,
      avatarFileId: avatarFileId.value
    })
    fillInfo()
    message.success('资料已保存')
  } catch {
    /* 拦截器已提示 */
  } finally {
    saving.value = false
  }
}

async function customUpload({ file }: UploadCustomRequestOptions): Promise<void> {
  const raw = file.file
  if (!raw) return
  uploading.value = true
  try {
    avatarFileId.value = await uploadAvatar(raw)
    // /auth/avatar 服务端已当场写入 sys_user.avatar_file_id，重载 profile 即拿到新头像（无需再点保存）
    await user.loadProfile(true)
    message.success('头像已更新')
  } catch {
    /* 拦截器已提示 */
  } finally {
    uploading.value = false
  }
}

async function submitPassword(): Promise<void> {
  const invalid = await pwdFormRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return
  saving.value = true
  try {
    await user.changePwd({ oldPassword: pwdModel.oldPassword, newPassword: pwdModel.newPassword })
    pwdModel.oldPassword = ''
    pwdModel.newPassword = ''
    pwdModel.confirmPassword = ''
    message.success('密码已修改，其它设备的登录态已失效')
    await user.loadProfile(true)
    await loadSessions()
  } catch {
    /* 拦截器已提示 */
  } finally {
    saving.value = false
  }
}

watch(
  () => route.query.tab,
  (value) => {
    if (value === 'password' || value === 'sessions' || value === 'info') activeTab.value = value
  }
)

watch(activeTab, (tab) => {
  if (tab === 'sessions' && !sessions.value.length) void loadSessions()
})

onMounted(async () => {
  if (!user.loaded) await user.loadProfile().catch(() => undefined)
  fillInfo()
  if (route.query.tab === 'password') activeTab.value = 'password'
})
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <template #header>
        <NSpace align="center" :size="12">
          <NAvatar round :size="44" :src="user.avatarUrl || undefined" color="#2563eb">{{ (user.displayName || '?').slice(0, 1) }}</NAvatar>
          <div>
            <div style="font-size: 16px; font-weight: 600">{{ user.displayName }}</div>
            <div class="ps-muted" style="font-size: 12px">
              @{{ user.userName }} · {{ user.profile?.deptName || '未分配部门' }}
            </div>
          </div>
          <NTag v-if="user.isAdmin" type="error" size="small" :bordered="false">内置管理员</NTag>
        </NSpace>
      </template>

      <NTabs v-model:value="activeTab" type="line" animated>
        <NTabPane name="info" tab="基本资料">
          <div class="ps-profile">
            <NForm ref="infoFormRef" :model="infoModel" :rules="infoRules" label-placement="left" label-width="92" class="ps-profile__form">
              <NFormItem label="昵称" path="nickName">
                <NInput v-model:value="infoModel.nickName" maxlength="64" placeholder="请输入昵称" />
              </NFormItem>
              <NFormItem label="手机号" path="phone">
                <NInput v-model:value="infoModel.phone" maxlength="32" placeholder="选填" />
              </NFormItem>
              <NFormItem label="邮箱" path="email">
                <NInput v-model:value="infoModel.email" maxlength="128" placeholder="选填" />
              </NFormItem>
              <NFormItem label="头像">
                <NSpace align="center">
                  <NAvatar round :size="32" :src="user.avatarUrl || undefined" color="#2563eb">{{ (user.displayName || '?').slice(0, 1) }}</NAvatar>
                  <NUpload :show-file-list="false" accept="image/png,image/jpeg,image/gif,image/webp" :custom-request="customUpload">
                    <NButton size="small" :loading="uploading">选择图片上传</NButton>
                  </NUpload>
                  <span class="ps-muted">{{ user.avatarUrl ? '已设置' : '未设置' }}</span>
                </NSpace>
              </NFormItem>
              <NFormItem :show-label="false">
                <NButton type="primary" :loading="saving" @click="saveInfo">保存资料</NButton>
              </NFormItem>
            </NForm>

            <div class="ps-profile__side">
              <NDescriptions label-placement="left" :column="1" bordered size="small" title="账号信息">
                <NDescriptionsItem label="用户名">{{ user.profile?.userName ?? '-' }}</NDescriptionsItem>
                <NDescriptionsItem label="部门">{{ user.profile?.deptName ?? '-' }}</NDescriptionsItem>
                <NDescriptionsItem label="角色">
                  <NSpace :size="4">
                    <NTag v-for="role in user.roles" :key="role" size="small" :bordered="false">{{ role }}</NTag>
                    <span v-if="!user.roles.length" class="ps-muted">未分配角色</span>
                  </NSpace>
                </NDescriptionsItem>
                <NDescriptionsItem label="权限码">{{ permissions }} 项</NDescriptionsItem>
                <NDescriptionsItem label="密码更新">
                  {{ formatDateTime(user.profile?.pwdUpdateTime) }}
                </NDescriptionsItem>
              </NDescriptions>
              <div class="ps-profile__avatar-preview">
                <div class="ps-profile__avatar-preview-title">头像预览</div>
                <NAvatar v-if="user.avatarUrl" round :size="72" :src="user.avatarUrl" />
                <NSpace v-else align="center" :size="8">
                  <NAvatar round :size="72" color="#2563eb">{{ (user.displayName || '?').slice(0, 1) }}</NAvatar>
                  <span class="ps-muted">尚未设置头像，上方显示的是昵称首字母</span>
                </NSpace>
              </div>
            </div>
          </div>
        </NTabPane>

        <NTabPane name="password" tab="修改密码">
          <NAlert type="warning" :bordered="false" style="margin-bottom: 16px">
            修改成功后其它设备的会话会被强制下线（当前会话保留）。
          </NAlert>
          <NForm ref="pwdFormRef" :model="pwdModel" :rules="pwdRules" label-placement="left" label-width="92" style="max-width: 460px">
            <NFormItem label="原密码" path="oldPassword">
              <NInput v-model:value="pwdModel.oldPassword" type="password" show-password-on="click" autocomplete="off" />
            </NFormItem>
            <NFormItem label="新密码" path="newPassword">
              <NInput v-model:value="pwdModel.newPassword" type="password" show-password-on="click" autocomplete="off" />
            </NFormItem>
            <NFormItem label="确认密码" path="confirmPassword">
              <NInput
                v-model:value="pwdModel.confirmPassword"
                type="password"
                show-password-on="click"
                autocomplete="off"
                @keyup.enter="submitPassword"
              />
            </NFormItem>
            <NFormItem :show-label="false">
              <NButton type="primary" :loading="saving" @click="submitPassword">确认修改</NButton>
            </NFormItem>
          </NForm>
        </NTabPane>

        <NTabPane name="sessions" tab="我的会话">
          <NSpace justify="space-between" align="center" style="margin-bottom: 10px">
            <span class="ps-muted">在线会话 {{ sessions.length }} 个（同账号互踢由后端参数控制）</span>
            <NButton size="small" :loading="sessionLoading" @click="loadSessions">刷新列表</NButton>
          </NSpace>
          <NDataTable
            :columns="sessionColumns"
            :data="sessions"
            :loading="sessionLoading"
            :row-key="(row: SessionDto) => row.id"
            size="small"
            :bordered="false"
          />
        </NTabPane>
      </NTabs>
    </NCard>
  </div>
</template>

<style scoped>
.ps-profile {
  display: flex;
  gap: 28px;
  flex-wrap: wrap;
}

.ps-profile__form {
  flex: 1 1 380px;
  max-width: 520px;
}

.ps-profile__side {
  flex: 1 1 320px;
  min-width: 300px;
}

.ps-profile__avatar-preview {
  margin-top: 14px;
  padding: 14px;
  border: 1px solid var(--ps-card-border);
  border-radius: 10px;
}

.ps-profile__avatar-preview-title {
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 10px;
}
</style>
