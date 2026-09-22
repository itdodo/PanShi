<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton, NCard, NForm, NFormItem, NInput, type FormInst, type FormRules } from 'naive-ui'
import { useUserStore } from '@/stores/user'
import { useRealtime } from '@/composables/useRealtime'
import { message } from '@/utils/feedback'
import { resetKickFlag } from '@/utils/session'

/** 强制改密页：密码超期/被重置时由路由守卫拦到这里（不依赖菜单接口） */
const router = useRouter()
const user = useUserStore()
const { disconnect } = useRealtime()

const formRef = ref<FormInst | null>(null)
const loading = ref(false)
const model = reactive({ oldPassword: '', newPassword: '', confirmPassword: '' })

const rules: FormRules = {
  oldPassword: [{ required: true, message: '请输入原密码', trigger: ['input', 'blur'] }],
  newPassword: [
    { required: true, message: '请输入新密码', trigger: ['input', 'blur'] },
    {
      validator: (_rule: unknown, value: string) =>
        value && value.length >= 8 ? true : new Error('新密码至少 8 位（服务端另有强度策略）'),
      trigger: ['input', 'blur']
    },
    {
      validator: (_rule: unknown, value: string) =>
        !value || value !== model.oldPassword ? true : new Error('新密码不能与原密码相同'),
      trigger: ['input', 'blur']
    }
  ],
  confirmPassword: [
    { required: true, message: '请再次输入新密码', trigger: ['input', 'blur'] },
    {
      validator: (_rule: unknown, value: string) =>
        value === model.newPassword ? true : new Error('两次输入的密码不一致'),
      trigger: ['input', 'blur']
    }
  ]
}

async function submit(): Promise<void> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return
  loading.value = true
  try {
    await user.changePwd({ oldPassword: model.oldPassword, newPassword: model.newPassword })
    message.success('密码修改成功，请使用新密码重新登录')
    await disconnect()
    user.resetAll()
    resetKickFlag()
    await router.replace('/login')
  } catch {
    /* 提示已由 http 拦截器统一处理 */
  } finally {
    loading.value = false
  }
}

async function backToLogin(): Promise<void> {
  await user.logout()
  resetKickFlag()
  await router.replace('/login')
}
</script>

<template>
  <div class="ps-force-pwd">
    <NCard class="ps-force-pwd__card" :bordered="false">
      <template #header>修改初始密码</template>
      <NAlert type="warning" :bordered="false" style="margin-bottom: 18px">
        您的密码已过期或由管理员重置，修改成功后需使用新密码重新登录。
      </NAlert>
      <NForm ref="formRef" :model="model" :rules="rules" label-placement="top" size="large">
        <NFormItem label="原密码" path="oldPassword">
          <NInput v-model:value="model.oldPassword" type="password" show-password-on="click" autocomplete="off" />
        </NFormItem>
        <NFormItem label="新密码" path="newPassword">
          <NInput v-model:value="model.newPassword" type="password" show-password-on="click" autocomplete="off" />
        </NFormItem>
        <NFormItem label="确认新密码" path="confirmPassword">
          <NInput
            v-model:value="model.confirmPassword"
            type="password"
            show-password-on="click"
            autocomplete="off"
            @keyup.enter="submit"
          />
        </NFormItem>
      </NForm>
      <div class="ps-force-pwd__actions">
        <NButton @click="backToLogin">退出登录</NButton>
        <NButton type="primary" :loading="loading" @click="submit">确认修改</NButton>
      </div>
    </NCard>
  </div>
</template>

<style scoped>
.ps-force-pwd {
  display: grid;
  place-items: center;
  min-height: 100%;
  padding: 40px 16px;
  background: var(--ps-page-bg);
}

.ps-force-pwd__card {
  width: 100%;
  max-width: 420px;
  box-shadow: 0 12px 40px rgba(15, 23, 42, 0.1);
}

.ps-force-pwd__actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 6px;
}
</style>
