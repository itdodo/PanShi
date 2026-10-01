<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import dayjs from 'dayjs'
import {
  NAlert,
  NButton,
  NCard,
  NDataTable,
  NDatePicker,
  NForm,
  NFormItem,
  NInput,
  NModal,
  NPopconfirm,
  NRadioButton,
  NRadioGroup,
  NSelect,
  NSpace,
  NSwitch,
  NTag,
  type DataTableColumns,
  type FormInst,
  type FormRules
} from 'naive-ui'
import {
  createIpRule,
  deleteIpRule,
  ipGuardStatus,
  pageIpRules,
  updateIpRule,
  type IpGuardStatus,
  type IpRuleDto,
  type IpRuleForm
} from '@/api/admin'
import AppIcon from '@/components/AppIcon.vue'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'

/**
 * IP 黑白名单（/monitor/ip-rule，安全 P1）。
 * 判定顺序 白 → 黑 → 未命中，白名单同时豁免限流。
 * DryRun / Enabled 是 appsettings 级开关（不在 sys_config），本页只能读不能改，故置顶提示。
 */
const IP_BLACK = 1
const IP_WHITE = 2
const KIND_OPTIONS = [
  { label: '黑名单', value: IP_BLACK },
  { label: '白名单', value: IP_WHITE }
]
/** EnableStatus：0 正常 / 1 停用 */
const STATUS_OPTIONS = [
  { label: '启用', value: 0 },
  { label: '停用', value: 1 }
]

type RuleQuery = { keyword: string; kind: number | null; status: number | null }

const list = usePageList<IpRuleDto, RuleQuery>({
  fetcher: pageIpRules,
  defaultQuery: () => ({ keyword: '', kind: null, status: null }),
  pageSize: 20
})

/* ------------------------------- 生效形态 ------------------------------- */
const status = ref<IpGuardStatus | null>(null)
const statusLoading = ref(false)

async function loadStatus(): Promise<void> {
  statusLoading.value = true
  try {
    status.value = await ipGuardStatus()
  } catch {
    status.value = null
  } finally {
    statusLoading.value = false
  }
}

const banner = computed(() => {
  const s = status.value
  if (!s) return null
  if (!s.enabled)
    return {
      type: 'error' as const,
      text: '名单功能已关闭（Security:IpGuard:Enabled = false），下表规则当前不参与任何判定。'
    }
  if (s.dryRun)
    return {
      type: 'warning' as const,
      text: '当前为 DryRun：黑名单命中只写日志、不拦截。先用它看清会不会误杀，确认后再把 Security:IpGuard:DryRun 设为 false 才真正生效。'
    }
  return { type: 'success' as const, text: '拦截已生效：命中黑名单的来源会被直接拒绝（白名单优先放行并豁免限流）。' }
})

/* -------------------------------- 编辑弹窗 -------------------------------- */
const modalVisible = ref(false)
const saving = ref(false)
const formRef = ref<FormInst | null>(null)
const editing = computed(() => !!form.id)

const form = reactive({
  id: null as string | null,
  cidr: '',
  kind: IP_BLACK,
  status: 0,
  reason: '',
  /** NDatePicker 用毫秒，提交时格式化；null = 永久 */
  expires: null as number | null,
  version: 0
})

/**
 * 只做字面量级校验，octet ≤255 / 前缀是否越界由后端 IPNetwork.TryParse 定夺。
 * IPv6 分支允许点号：容器 NAT 下真实来源就长这样（::ffff:172.19.0.1），不给过等于封不了它。
 */
const CIDR_PATTERN =
  /^(?:\d{1,3}(?:\.\d{1,3}){3}(?:\/(?:3[0-2]|[12][0-9]|[0-9]))?|(?:[0-9A-Fa-f]{0,4}:)[0-9A-Fa-f:.]*(?:\/(?:12[0-8]|1[01][0-9]|[1-9]?[0-9]))?)$/

const rules: FormRules = {
  cidr: [
    { required: true, message: '请输入 IP 或 CIDR', trigger: ['input', 'blur'] },
    { pattern: CIDR_PATTERN, message: '格式不正确，例：203.0.113.7 或 172.16.0.0/12', trigger: ['input', 'blur'] }
  ],
  reason: [{ max: 256, message: '原因最多 256 字', trigger: ['input', 'blur'] }]
}

function openCreate(): void {
  form.id = null
  form.cidr = ''
  form.kind = IP_BLACK
  form.status = 0
  form.reason = ''
  form.expires = null
  form.version = 0
  modalVisible.value = true
}

function openEdit(row: IpRuleDto): void {
  form.id = row.id
  form.cidr = row.cidr
  form.kind = row.kind
  form.status = row.status
  form.reason = row.reason ?? ''
  form.expires = row.expiresTime ? dayjs(row.expiresTime).valueOf() : null
  form.version = row.version
  modalVisible.value = true
}

function toPayload(): IpRuleForm {
  return {
    cidr: form.cidr.trim(),
    kind: form.kind,
    status: form.status,
    reason: form.reason.trim() || null,
    expiresTime: form.expires ? dayjs(form.expires).format('YYYY-MM-DDTHH:mm:ss') : null
  }
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  try {
    if (editing.value && form.id) await updateIpRule(form.id, { ...toPayload(), version: form.version })
    else await createIpRule(toPayload())
    message.success(editing.value ? '规则已保存，立即生效' : '规则已新增，立即生效')
    await list.load()
    return true
  } catch {
    // 拦截器已把「覆盖受信代理/来源地址」这类防自锁报错弹出来；返回 false 让弹窗保持打开
    return false
  } finally {
    saving.value = false
  }
}

async function remove(row: IpRuleDto): Promise<void> {
  try {
    await deleteIpRule(row.id)
    message.success('已删除，判定缓存同步失效')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

/* -------------------------------- 表格列 -------------------------------- */
const columns = computed<DataTableColumns<IpRuleDto>>(() => [
  { title: 'IP / CIDR', key: 'cidr', width: 180 },
  {
    title: '类型',
    key: 'kind',
    width: 96,
    render: (row) =>
      h(
        NTag,
        { size: 'small', bordered: false, type: row.kind === IP_WHITE ? 'success' : 'error' },
        { default: () => (row.kind === IP_WHITE ? '白名单' : '黑名单') }
      )
  },
  {
    title: '来源',
    key: 'source',
    width: 88,
    render: (row) => h('span', { class: 'ps-muted' }, row.source === 2 ? '自动' : '人工')
  },
  {
    title: '状态',
    key: 'status',
    width: 88,
    render: (row) =>
      h(
        NTag,
        { size: 'small', bordered: false, type: row.status === 1 ? 'default' : 'info' },
        { default: () => (row.status === 1 ? '停用' : '启用') }
      )
  },
  {
    title: '命中',
    key: 'hitCount',
    width: 90,
    render: (row) => {
      const n = Number(row.hitCount ?? 0) || 0
      return h('span', { class: n > 0 ? 'ps-ipguard__hits' : 'ps-muted' }, String(n))
    }
  },
  {
    title: '最近命中',
    key: 'lastHitTime',
    width: 165,
    render: (row) => formatDateTime(row.lastHitTime)
  },
  {
    title: '到期',
    key: 'expiresTime',
    width: 165,
    render: (row) =>
      row.expiresTime
        ? h(
            NTag,
            { size: 'small', bordered: false, type: dayjs(row.expiresTime).isBefore(dayjs()) ? 'warning' : 'default' },
            { default: () => formatDateTime(row.expiresTime) }
          )
        : h('span', { class: 'ps-muted' }, '永久')
  },
  { title: '原因', key: 'reason', minWidth: 180, ellipsis: { tooltip: true }, render: (row) => row.reason || '-' },
  { title: '创建时间', key: 'createTime', width: 165, sorter: true, render: (row) => formatDateTime(row.createTime) },
  {
    title: '操作',
    key: 'actions',
    width: 120,
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm('monitor:ipguard:manage')
            ? h(NButton, { key: 'edit', size: 'tiny', text: true, type: 'primary', onClick: () => openEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('monitor:ipguard:manage')
            ? h(
                NPopconfirm,
                { key: 'delete', onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除「${row.cidr}」？判定缓存会立即失效。`
                }
              )
            : null
        ].filter(Boolean)
      })
  }
])

onMounted(() => {
  void loadStatus()
})
</script>

<template>
  <div class="ps-page">
    <NAlert v-if="banner" :type="banner.type" :bordered="false" class="ps-ipguard__banner" :show-icon="true">
      <NSpace align="center" :size="10" :wrap="true">
        <span>{{ banner.text }}</span>
        <NButton size="tiny" tertiary :loading="statusLoading" @click="loadStatus">刷新状态</NButton>
      </NSpace>
    </NAlert>

    <NCard :bordered="false">
      <template #header>
        <NSpace align="center" :size="8">
          <AppIcon name="lucide:shield" :size="18" />
          <span>IP 黑白名单</span>
        </NSpace>
      </template>

      <NForm inline :model="list.queryParams" label-placement="left" :show-feedback="false">
        <NSpace :size="12" align="center" wrap>
          <NFormItem label="关键字">
            <NInput
              v-model:value="list.queryParams.keyword"
              placeholder="IP / CIDR / 原因"
              clearable
              style="width: 220px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="类型">
            <NSelect
              v-model:value="list.queryParams.kind"
              :options="KIND_OPTIONS"
              placeholder="全部"
              clearable
              style="width: 130px"
            />
          </NFormItem>
          <NFormItem label="状态">
            <NSelect
              v-model:value="list.queryParams.status"
              :options="STATUS_OPTIONS"
              placeholder="全部"
              clearable
              style="width: 120px"
            />
          </NFormItem>
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="list.reset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" class="ps-ipguard__toolbar" :wrap="true">
        <NButton v-permission="'monitor:ipguard:manage'" type="primary" @click="openCreate">新增规则</NButton>
        <span class="ps-muted">共 {{ list.total.value }} 条 · 判定顺序：白名单 → 黑名单 → 未命中放行</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: IpRuleDto) => row.id"
        :scroll-x="1450"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑规则' : '新增规则'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 560px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="92" class="ps-ipguard__form">
        <NFormItem label="IP / CIDR" path="cidr">
          <NInput v-model:value="form.cidr" maxlength="64" placeholder="203.0.113.7 或 172.16.0.0/12" />
        </NFormItem>
        <NFormItem label="类型" path="kind">
          <NRadioGroup v-model:value="form.kind">
            <NRadioButton :value="IP_BLACK">黑名单（拒绝）</NRadioButton>
            <NRadioButton :value="IP_WHITE">白名单（放行并豁免限流）</NRadioButton>
          </NRadioGroup>
        </NFormItem>
        <NFormItem label="状态" path="status">
          <NSwitch :value="form.status === 0" @update:value="(v: boolean) => (form.status = v ? 0 : 1)">
            <template #checked>启用</template>
            <template #unchecked>停用</template>
          </NSwitch>
        </NFormItem>
        <NFormItem label="到期时间" path="expires">
          <NDatePicker v-model:value="form.expires" type="datetime" clearable style="width: 220px" />
        </NFormItem>
        <NFormItem label="原因" path="reason">
          <NInput v-model:value="form.reason" type="textarea" :rows="2" maxlength="256" show-count placeholder="为何封禁 / 为何加白" />
        </NFormItem>
      </NForm>
      <p class="ps-muted ps-ipguard__hint">
        裸 IP 会自动补 /32（IPv6 补 /128）。封禁覆盖受信代理或本网关地址的规则会被后端拒绝——
        直连与容器 NAT 拓扑下所有客户端都显示为同一个来源，封它就是封全站。
      </p>
    </NModal>
  </div>
</template>

<style scoped>
.ps-ipguard__banner {
  margin-bottom: 12px;
}

.ps-ipguard__toolbar {
  margin: 16px 0 12px;
}

.ps-ipguard__form {
  margin-top: 6px;
}

.ps-ipguard__hint {
  margin: 4px 0 0;
  line-height: 1.6;
}

.ps-ipguard__hits {
  font-weight: 600;
}
</style>
