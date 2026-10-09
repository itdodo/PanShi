<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NForm, NFormItem, NInput, type FormInst, type FormRules } from 'naive-ui'
import { getCaptcha } from '@/api/auth'
import { useUserStore } from '@/stores/user'
import { message } from '@/utils/feedback'
import { resetKickFlag } from '@/utils/session'
import { isDark, toggleDark } from '@/utils/themeState'
import { FORCE_CHANGE_PWD_PATH } from '@/router'

/**
 * 登录页：分屏品牌式（左深色品牌区 + 右表单），亮暗自适应，≤900px 隐藏品牌区。
 * 不预填账号密码；验证码点击刷新（blob + X-Captcha-Id），后端 X-Captcha-Enabled: 0 时整行隐藏。
 */
const route = useRoute()
const router = useRouter()
const user = useUserStore()

const formRef = ref<FormInst | null>(null)
const loading = ref(false)
const model = reactive({ userName: '', password: '', captchaCode: '' })
const captcha = reactive({ enabled: true, objectUrl: '', id: '', loading: false })

const rules: FormRules = {
  userName: [{ required: true, message: '请输入用户名', trigger: ['input', 'blur'] }],
  password: [{ required: true, message: '请输入密码', trigger: ['input', 'blur'] }]
}

const redirect = computed(() => (typeof route.query.redirect === 'string' ? route.query.redirect : '/'))

async function refreshCaptcha(): Promise<void> {
  if (captcha.objectUrl) URL.revokeObjectURL(captcha.objectUrl)
  captcha.objectUrl = ''
  captcha.id = ''
  captcha.loading = true
  try {
    const result = await getCaptcha()
    captcha.enabled = result.enabled
    captcha.objectUrl = result.objectUrl
    captcha.id = result.captchaId
  } catch {
    // 后端未就绪 / 接口失败：留空即可，登录接口容忍空验证码
    captcha.enabled = true
    captcha.objectUrl = ''
    captcha.id = ''
  } finally {
    captcha.loading = false
  }
}

void refreshCaptcha()

onBeforeUnmount(() => {
  if (captcha.objectUrl) URL.revokeObjectURL(captcha.objectUrl)
})

async function doLogin(): Promise<void> {
  loading.value = true
  try {
    const result = await user.login({
      userName: model.userName.trim(),
      password: model.password,
      captchaId: captcha.id || undefined,
      captchaCode: model.captchaCode || undefined
    })
    resetKickFlag()
    message.success('登录成功')
    if (result.mustChangePassword) {
      await router.replace(FORCE_CHANGE_PWD_PATH)
      return
    }
    await router.replace(redirect.value || '/')
  } catch {
    // 错误提示由 http 拦截器统一弹出；验证码一次性，失败即刷新
    model.captchaCode = ''
    await refreshCaptcha()
  } finally {
    loading.value = false
  }
}

function handleSubmit(): void {
  formRef.value
    ?.validate()
    .then(() => doLogin())
    .catch(() => {
      /* 校验未通过：表单已就地提示 */
    })
}
</script>

<template>
  <div class="ps-login">
    <!-- 左：品牌区 -->
    <section class="ps-login__brand">
      <div class="ps-login__brand-inner">
        <div class="ps-login__logo">
          <icon-lucide-hexagon />
          <span>磐石</span>
        </div>
        <h1 class="ps-login__slogan">磐石管理系统</h1>
        <p class="ps-login__desc">
          统一权限 · 组织架构 · 审批流程 · 公告与消息<br />登录后能用什么，就看你被授予了哪些功能。
        </p>
        <ul class="ps-login__features">
          <li><icon-lucide-shield-check /> 按角色授权：看得到哪些菜单、管得到哪些数据</li>
          <li><icon-lucide-git-pull-request-arrow /> 审批流：多人会签、依次审批、加签与驳回</li>
          <li><icon-lucide-file-text /> 上传文件按类型把关，每次改动都留下可追溯的记录</li>
        </ul>
      </div>
      <!-- 抽象图形（纯装饰，无外部资源） -->
      <svg class="ps-login__art" viewBox="0 0 600 600" aria-hidden="true">
        <defs>
          <linearGradient id="ps-g1" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0%" stop-color="#2563eb" stop-opacity="0.75" />
            <stop offset="100%" stop-color="#0ea5e9" stop-opacity="0.12" />
          </linearGradient>
          <linearGradient id="ps-g2" x1="1" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="#60a5fa" stop-opacity="0.5" />
            <stop offset="100%" stop-color="#1e3a8a" stop-opacity="0.05" />
          </linearGradient>
        </defs>
        <circle cx="430" cy="150" r="190" fill="url(#ps-g1)" />
        <rect x="60" y="300" width="260" height="260" rx="48" fill="url(#ps-g2)" transform="rotate(-16 190 430)" />
        <path d="M300 520 L470 350 L560 440 L390 610 Z" fill="#3d7bf4" opacity="0.14" />
        <g stroke="#93c5fd" stroke-opacity="0.35" fill="none" stroke-width="2">
          <circle cx="150" cy="150" r="60" />
          <circle cx="150" cy="150" r="104" />
          <path d="M150 46 L150 254 M46 150 L254 150" />
        </g>
      </svg>
    </section>

    <!-- 右：表单 -->
    <section class="ps-login__panel">
      <div class="ps-login__panel-head">
        <div>
          <h2 class="ps-login__title">欢迎回来</h2>
          <p class="ps-login__sub">请使用工作账号登录</p>
        </div>
        <NButton quaternary circle size="small" :aria-label="isDark ? '切换亮色模式' : '切换暗色模式'" @click="toggleDark()">
          <icon-lucide-sun v-if="isDark" />
          <icon-lucide-moon v-else />
        </NButton>
      </div>

      <NForm ref="formRef" :model="model" :rules="rules" size="large" @submit.prevent>
        <NFormItem label="用户名" path="userName">
          <NInput
            v-model:value="model.userName"
            placeholder="请输入用户名"
            autocomplete="off"
            clearable
          >
            <template #prefix><icon-lucide-user-round class="ps-login__icon" /></template>
          </NInput>
        </NFormItem>

        <NFormItem label="密码" path="password">
          <NInput
            v-model:value="model.password"
            type="password"
            placeholder="请输入密码"
            autocomplete="new-password"
            show-password-on="click"
          >
            <template #prefix><icon-lucide-lock class="ps-login__icon" /></template>
          </NInput>
        </NFormItem>

        <NFormItem v-if="captcha.enabled" label="验证码" path="captchaCode">
          <div class="ps-login__captcha-row">
            <NInput
              v-model:value="model.captchaCode"
              placeholder="图形验证码"
              autocomplete="off"
              :maxlength="8"
              @keyup.enter="handleSubmit"
            >
              <template #prefix><icon-lucide-shield class="ps-login__icon" /></template>
            </NInput>
            <button
              type="button"
              class="ps-login__captcha"
              title="点击刷新验证码"
              :disabled="captcha.loading"
              @click="refreshCaptcha"
            >
              <img v-if="captcha.objectUrl" :src="captcha.objectUrl" alt="验证码" />
              <span v-else>{{ captcha.loading ? '加载中' : '点击获取' }}</span>
            </button>
          </div>
        </NFormItem>

        <NButton type="primary" size="large" block :loading="loading" @click="handleSubmit"> 登 录 </NButton>
      </NForm>

      <p v-if="captcha.enabled" class="ps-login__tip">
        验证码可在后端参数 <code>sys.captcha.enabled</code> 关闭，关闭后本行自动隐藏。
      </p>

      <footer class="ps-login__foot">© 2026 磐石 Panshi · 企业内部系统</footer>
    </section>
  </div>
</template>

<style scoped>
.ps-login {
  display: flex;
  height: 100%;
  overflow: hidden;
}

/* —— 品牌区 —— */
.ps-login__brand {
  position: relative;
  flex: 1 1 52%;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  background: linear-gradient(140deg, #0b1220 0%, #10203a 45%, #0f172a 100%);
  color: #e2e8f0;
}

.ps-login__brand-inner {
  position: relative;
  z-index: 1;
  max-width: 460px;
  padding: 40px 44px;
}

.ps-login__logo {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 15px;
  font-weight: 600;
  letter-spacing: 3px;
  color: #93c5fd;
  text-transform: uppercase;
}

.ps-login__logo :deep(svg) {
  width: 22px;
  height: 22px;
}

.ps-login__slogan {
  margin: 22px 0 12px;
  font-size: 34px;
  line-height: 1.25;
  font-weight: 700;
  color: #f8fafc;
}

.ps-login__desc {
  margin: 0 0 26px;
  font-size: 14px;
  line-height: 1.8;
  color: rgba(203, 213, 225, 0.82);
}

.ps-login__features {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 12px;
  font-size: 13px;
  color: rgba(203, 213, 225, 0.9);
}

.ps-login__features li {
  display: flex;
  align-items: center;
  gap: 10px;
}

.ps-login__features :deep(svg) {
  width: 16px;
  height: 16px;
  color: #60a5fa;
  flex: none;
}

.ps-login__art {
  position: absolute;
  right: -60px;
  bottom: -80px;
  width: 620px;
  height: 620px;
  opacity: 0.75;
  pointer-events: none;
}

/* —— 表单区 —— */
.ps-login__panel {
  flex: 0 0 460px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  padding: 40px 52px;
  background: var(--ps-page-bg);
}

.ps-login__panel-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 26px;
}

.ps-login__title {
  margin: 0;
  font-size: 24px;
  font-weight: 650;
}

.ps-login__sub {
  margin: 6px 0 0;
  font-size: 13px;
  color: var(--ps-text-3);
}

.ps-login__icon {
  width: 16px;
  height: 16px;
}

.ps-login__captcha-row {
  display: flex;
  gap: 10px;
  width: 100%;
}

.ps-login__captcha {
  flex: none;
  width: 112px;
  height: 40px;
  padding: 0;
  overflow: hidden;
  border: 1px solid rgba(128, 128, 128, 0.28);
  border-radius: 8px;
  background: rgba(128, 128, 128, 0.06);
  color: var(--ps-text-3);
  font-size: 12px;
  cursor: pointer;
  display: grid;
  place-items: center;
}

.ps-login__captcha img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.ps-login__tip {
  margin: 18px 0 0;
  font-size: 12px;
  color: var(--ps-text-3);
  line-height: 1.7;
}

.ps-login__foot {
  margin-top: 30px;
  font-size: 12px;
  color: var(--ps-text-3);
}

@media (max-width: 900px) {
  .ps-login__brand {
    display: none;
  }

  .ps-login__panel {
    flex: 1 1 auto;
    padding: 32px 24px;
  }
}
</style>
