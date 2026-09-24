<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import {
  NAlert,
  NButton,
  NCard,
  NCheckbox,
  NDataTable,
  NForm,
  NFormItem,
  NInput,
  NModal,
  NPopconfirm,
  NRadio,
  NRadioGroup,
  NSelect,
  NSpace,
  NTag,
  NTreeSelect,
  NUpload,
  type DataTableColumns,
  type FormInst,
  type FormRules,
  type TreeOption,
  type UploadCustomRequestOptions
} from 'naive-ui'
import { del, download, post, put, upload } from '@/api/http'
import { errorText } from '@/api/types'
import { pageUsers, type UserDto } from '@/api/system/user'
import { getDeptTree } from '@/api/system/dept'
import { positionOptions, roleOptions, type Option } from '@/api/admin'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { dialog, message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'
import { STATUS_OPTIONS, pruneChildren, statusTag, toId, toIds, toNum, toStrIds, toTreeOptions } from '../_shared'

/**
 * 用户管理（/sys/user）。
 * 契约以已联调后端为准：列表 pageUsers；新增 POST /sys/user；编辑 PUT /sys/user/{id}（必带 version）；
 * 删除 DELETE /sys/user/{id}；重置密码 POST /sys/user/{id}/password/reset（回传初始密码）；
 * 导入 POST /sys/user/import(multipart file) / 模板 GET /sys/user/import-template / 导出 GET /sys/user/export。
 * src/api/system/user.ts 里的 update/delete/reset 预声明地址与后端不一致，按规约在视图内直调 http。
 */
type UserRow = UserDto & { roleIds?: string[]; positionIds?: string[] }
type UserQueryModel = {
  keyword: string
  deptId: string | null
  status: number | null
  deptWithChildren: boolean
}

type UserFormModel = {
  id: string | null
  userName: string
  nickName: string
  password: string
  phone: string
  email: string
  deptId: string | null
  status: number
  remark: string
  roleIds: string[]
  positionIds: string[]
  version: number
}

interface ImportResult {
  total: number
  success: number
  errors?: string[] | null
}

const list = usePageList<UserRow, UserQueryModel>({
  fetcher: pageUsers,
  defaultQuery: () => ({ keyword: '', deptId: null, status: null, deptWithChildren: true })
})

/* ------------------------------ 下拉数据源 ------------------------------ */
const deptNodes = ref<TreeOption[]>([])
const roleList = ref<Option[]>([])
const positionList = ref<Option[]>([])

const roleMap = computed(() => new Map(roleList.value.map((item) => [item.value, item.label])))
const roleSelectOptions = computed(() => roleList.value.map((item) => ({ label: item.label, value: item.value })))
const positionSelectOptions = computed(() =>
  positionList.value.map((item) => ({ label: item.label, value: item.value }))
)

async function loadOptions(): Promise<void> {
  try {
    const tree = await getDeptTree()
    deptNodes.value = toTreeOptions(
      pruneChildren(tree ?? []),
      (node) => node.id,
      (node) => node.deptName,
      (node) => node.children
    )
  } catch {
    deptNodes.value = []
  }
  roleList.value = await roleOptions().catch(() => [])
  positionList.value = await positionOptions().catch(() => [])
}

/* -------------------------------- 编辑弹窗 -------------------------------- */
const modalVisible = ref(false)
const saving = ref(false)
const editing = computed(() => !!form.id)
const formRef = ref<FormInst | null>(null)

const form = reactive<UserFormModel>({
  id: null,
  userName: '',
  nickName: '',
  password: '',
  phone: '',
  email: '',
  deptId: null,
  status: 0,
  remark: '',
  roleIds: [],
  positionIds: [],
  version: 0
})

const rules = computed<FormRules>(() => ({
  userName: editing.value ? [] : [{ required: true, min: 2, max: 64, message: '用户名 2-64 个字符', trigger: ['input', 'blur'] }],
  nickName: [{ required: true, max: 64, message: '请输入昵称', trigger: ['input', 'blur'] }],
  phone: [
    {
      validator: (_rule: unknown, value: string) =>
        !value || /^1[3-9]\d{9}$/.test(value) ? true : new Error('手机号格式不正确'),
      trigger: ['input', 'blur']
    }
  ],
  email: [
    {
      validator: (_rule: unknown, value: string) =>
        !value || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value) ? true : new Error('邮箱格式不正确'),
      trigger: ['input', 'blur']
    }
  ]
}))

function resetForm(): void {
  form.id = null
  form.userName = ''
  form.nickName = ''
  form.password = ''
  form.phone = ''
  form.email = ''
  form.deptId = null
  form.status = 0
  form.remark = ''
  form.roleIds = []
  form.positionIds = []
  form.version = 0
}

function openCreate(): void {
  resetForm()
  modalVisible.value = true
}

function openEdit(row: UserRow): void {
  form.id = row.id
  form.userName = row.userName
  form.nickName = row.nickName
  form.password = ''
  form.phone = row.phone ?? ''
  form.email = row.email ?? ''
  form.deptId = row.deptId ?? null
  form.status = row.status
  form.remark = row.remark ?? ''
  form.roleIds = [...(row.roleIds ?? [])]
  form.positionIds = [...(row.positionIds ?? [])]
  form.version = row.version
  modalVisible.value = true
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  const payload = {
    nickName: form.nickName.trim(),
    phone: form.phone.trim() || null,
    email: form.email.trim() || null,
    deptId: form.deptId,
    status: form.status,
    remark: form.remark.trim() || null,
    roleIds: toIds(form.roleIds),
    positionIds: toIds(form.positionIds)
  }
  try {
    if (editing.value && form.id) {
      await put(`/sys/user/${form.id}`, { ...payload, version: form.version })
      message.success('用户已保存')
    } else {
      await post('/sys/user', {
        ...payload,
        userName: form.userName.trim(),
        password: form.password || undefined
      })
      message.success('用户已新增')
    }
    await list.load()
    return true
  } catch {
    /* 拦截器已统一提示（含 409 冲突），弹窗保持打开供修正 */
    return false
  } finally {
    saving.value = false
  }
}

async function remove(row: UserRow): Promise<void> {
  try {
    await del(`/sys/user/${row.id}`)
    message.success('已删除')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

/* ------------------------------- 重置密码 ------------------------------- */
async function resetPassword(row: UserRow): Promise<void> {
  try {
    const pwd = await post<string>(`/sys/user/${row.id}/password/reset`)
    dialog.info({
      title: `已重置 ${row.userName} 的密码`,
      content: `初始密码：${pwd}（仅本次显示，请及时通知本人修改）`,
      positiveText: '我记住了'
    })
    await list.load()
  } catch {
    /* 已提示 */
  }
}

/* ------------------------------- 导入 / 导出 ------------------------------- */
const importing = ref(false)
const exporting = ref(false)
const importVisible = ref(false)
const importResult = ref<ImportResult | null>(null)
const importSummary = computed(() => {
  const result = importResult.value
  if (!result) return null
  return { total: toNum(result.total), success: toNum(result.success), errors: result.errors ?? [] }
})

async function customImport({ file }: UploadCustomRequestOptions): Promise<void> {
  const raw = file.file
  if (!raw) return
  importing.value = true
  try {
    const result = await upload<ImportResult>('/sys/user/import', raw)
    importResult.value = result ?? { total: 0, success: 0, errors: [] }
    importVisible.value = true
    await list.load()
  } catch (err) {
    message.error(errorText(err, '导入失败'))
  } finally {
    importing.value = false
  }
}

function downloadTemplate(): void {
  void download('/sys/user/import-template', undefined, '用户导入模板.xlsx').catch(() => undefined)
}

async function exportRows(): Promise<void> {
  exporting.value = true
  try {
    await download('/sys/user/export', { ...list.queryParams }, '用户.xlsx')
    message.success('导出已开始')
  } catch {
    /* 已提示 */
  } finally {
    exporting.value = false
  }
}

/* --------------------------------- 表格 --------------------------------- */
const columns = computed<DataTableColumns<UserRow>>(() => [
  { title: '用户名', key: 'userName', width: 130, sorter: true },
  { title: '昵称', key: 'nickName', width: 130, sorter: true },
  { title: '部门', key: 'deptName', width: 150, ellipsis: { tooltip: true }, render: (row) => row.deptName ?? '-' },
  {
    title: '角色',
    key: 'roleIds',
    minWidth: 180,
    render: (row) => {
      const ids = row.roleIds ?? []
      if (!ids.length) return h('span', { class: 'ps-muted' }, '未分配')
      return h(
        NSpace,
        { size: 4 },
        {
          default: () =>
            ids.map((id) =>
              h(NTag, { key: id, size: 'small', bordered: false, type: 'info' }, { default: () => roleMap.value.get(id) ?? id })
            )
        }
      )
    }
  },
  { title: '手机号', key: 'phone', width: 120, render: (row) => row.phone ?? '-' },
  {
    title: '状态',
    key: 'status',
    width: 84,
    render: (row) => {
      const tag = statusTag(row.status)
      return h(NTag, { size: 'small', type: tag.type, bordered: false }, { default: () => tag.label })
    }
  },
  {
    title: '最后登录',
    key: 'lastLoginTime',
    width: 165,
    sorter: true,
    render: (row) => formatDateTime(row.lastLoginTime)
  },
  {
    title: '操作',
    key: 'actions',
    width: 210,
    render: (row) => {
      const isBuiltInAdmin = row.userName === 'admin'
      const buttons = [
        hasPerm('sys:user:edit')
          ? h(NButton, { size: 'tiny', text: true, type: 'primary', onClick: () => openEdit(row) }, { default: () => '编辑' })
          : null,
        hasPerm('sys:user:resetpwd') && !isBuiltInAdmin
          ? h(
              NPopconfirm,
              { onPositiveClick: () => resetPassword(row) },
              {
                trigger: () => h(NButton, { size: 'tiny', text: true, type: 'warning' }, { default: () => '重置密码' }),
                default: () => '将重置为系统初始密码，确定？'
              }
            )
          : null,
        hasPerm('sys:user:delete') && !isBuiltInAdmin
          ? h(
              NPopconfirm,
              { onPositiveClick: () => remove(row) },
              {
                trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                default: () => `删除用户「${row.nickName}」？`
              }
            )
          : null
      ]
      return h(NSpace, { size: 10 }, { default: () => buttons })
    }
  }
])

onMounted(() => {
  void loadOptions()
})
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NForm inline :model="list.queryParams" label-placement="left" :show-feedback="false">
        <NSpace :size="12" align="center" wrap>
          <NFormItem label="关键字">
            <NInput
              v-model:value="list.queryParams.keyword"
              placeholder="用户名 / 昵称"
              clearable
              style="width: 200px"
              @keyup.enter="list.search"
            />
          </NFormItem>
          <NFormItem label="部门">
            <NTreeSelect
              :value="list.queryParams.deptId"
              :options="deptNodes"
              placeholder="全部部门"
              clearable
              filterable
              style="width: 200px"
              @update:value="list.queryParams.deptId = toId($event)"
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
          <NFormItem label="含子部门">
            <NCheckbox v-model:checked="list.queryParams.deptWithChildren" />
          </NFormItem>
          <NSpace :size="8">
            <NButton type="primary" @click="list.search">查询</NButton>
            <NButton tertiary @click="list.reset">重置</NButton>
          </NSpace>
        </NSpace>
      </NForm>

      <NSpace justify="space-between" align="center" style="margin: 16px 0 12px">
        <NSpace :size="8">
          <NButton v-permission="'sys:user:add'" type="primary" @click="openCreate">新增用户</NButton>
          <NUpload
            v-permission="'sys:user:import'"
            :show-file-list="false"
            accept=".xlsx,.xls"
            :custom-request="customImport"
          >
            <NButton :loading="importing">导入</NButton>
          </NUpload>
          <NButton v-permission="'sys:user:import'" tertiary @click="downloadTemplate">下载模板</NButton>
          <NButton v-permission="'sys:user:export'" tertiary :loading="exporting" @click="exportRows">导出</NButton>
        </NSpace>
        <span class="ps-muted">共 {{ list.total.value }} 条</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: UserRow) => row.id"
        :scroll-x="1180"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑用户' : '新增用户'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 620px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
      @after-leave="resetForm"
    >
      <NAlert v-if="!editing" type="info" :bordered="false" style="margin-bottom: 12px">
        密码留空则使用系统参数 sys.user.initPassword 作为初始密码。
      </NAlert>
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="82">
        <NFormItem label="用户名" path="userName">
          <NInput v-model:value="form.userName" :disabled="editing" maxlength="64" placeholder="登录账号" />
        </NFormItem>
        <NFormItem label="昵称" path="nickName">
          <NInput v-model:value="form.nickName" maxlength="64" placeholder="显示名" />
        </NFormItem>
        <NFormItem v-if="!editing" label="初始密码" path="password">
          <NInput v-model:value="form.password" type="password" show-password-on="click" autocomplete="off" placeholder="留空=系统初始密码" />
        </NFormItem>
        <NFormItem label="部门" path="deptId">
          <NTreeSelect
            :value="form.deptId"
            :options="deptNodes"
            placeholder="请选择部门"
            clearable
            filterable
            style="width: 100%"
            @update:value="form.deptId = toId($event)"
          />
        </NFormItem>
        <NFormItem label="角色" path="roleIds">
          <NSelect
            :value="form.roleIds"
            multiple
            :options="roleSelectOptions"
            placeholder="可多选（决定菜单与按钮权限）"
            @update:value="form.roleIds = toStrIds($event)"
          />
        </NFormItem>
        <NFormItem label="岗位" path="positionIds">
          <NSelect
            :value="form.positionIds"
            multiple
            :options="positionSelectOptions"
            placeholder="可多选（审批流岗位解析用）"
            @update:value="form.positionIds = toStrIds($event)"
          />
        </NFormItem>
        <NFormItem label="手机号" path="phone">
          <NInput v-model:value="form.phone" maxlength="32" placeholder="选填" />
        </NFormItem>
        <NFormItem label="邮箱" path="email">
          <NInput v-model:value="form.email" maxlength="128" placeholder="选填" />
        </NFormItem>
        <NFormItem label="状态" path="status">
          <NRadioGroup v-model:value="form.status">
            <NSpace :size="16">
              <NRadio v-for="item in STATUS_OPTIONS" :key="item.value" :value="item.value" :label="item.label" />
            </NSpace>
          </NRadioGroup>
        </NFormItem>
        <NFormItem label="备注" path="remark">
          <NInput v-model:value="form.remark" type="textarea" :rows="2" maxlength="512" show-count />
        </NFormItem>
      </NForm>
    </NModal>

    <NModal v-model:show="importVisible" preset="dialog" title="导入结果" positive-text="关闭" style="width: 520px">
      <template v-if="importSummary">
        <NAlert :type="importSummary.errors.length ? 'warning' : 'success'" :bordered="false">
          共 {{ importSummary.total }} 行，成功 {{ importSummary.success }} 行，失败
          {{ importSummary.total - importSummary.success }} 行。
        </NAlert>
        <ul v-if="importSummary.errors.length" class="ps-import-errors">
          <li v-for="(err, index) in importSummary.errors" :key="index">{{ err }}</li>
        </ul>
      </template>
    </NModal>
  </div>
</template>

<style scoped>
.ps-import-errors {
  margin: 12px 0 0;
  padding-left: 20px;
  max-height: 220px;
  overflow-y: auto;
  color: var(--ps-text-3);
  font-size: 12px;
  line-height: 1.8;
}
</style>
