<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import {
  NAlert,
  NButton,
  NCard,
  NDataTable,
  NForm,
  NFormItem,
  NGi,
  NGrid,
  NInput,
  NInputNumber,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NSwitch,
  NTag,
  NTree,
  NTreeSelect,
  type DataTableColumns,
  type FormInst,
  type FormRules,
  type TreeOption
} from 'naive-ui'
import {
  createRole,
  deleteRole,
  getRole,
  grantRoleMenus,
  pageRoles,
  updateRole,
  type RoleDto
} from '@/api/system/role'
import { getGrantMenuTree, type MenuTreeNode } from '@/api/menu'
import { getDeptTree } from '@/api/system/dept'
import { usePageList } from '@/composables/usePageList'
import { useUserStore } from '@/stores/user'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { formatDateTime } from '@/utils/format'
import { SCOPE_CUSTOM, SCOPE_OPTIONS, STATUS_OPTIONS, pruneChildren, scopeLabel, statusTag, toIds, toStrIds, toTreeOptions } from '../_shared'

/**
 * 角色管理（/sys/role）：分页 + CRUD + 「授权菜单」弹窗（菜单树勾选 + 数据权限五档 + 自定义部门）。
 * 后端 PUT /sys/role/{id}（RoleUpdateDto 含 version）；授权走 POST /sys/role/{id}/menus（GrantMenusDto，
 * 无需回传 version，避免与基础信息编辑互相覆盖）。列表不回传 menuIds/deptIds，弹窗打开时用 GET /sys/role/{id} 取。
 * 授权树的菜单结构走 GET /sys/menu/tree/grant（登录即可），不走 sys:menu:list 的全量树。
 */
type RoleRow = RoleDto
/** GET /sys/role/{id} 额外回传已授权 menuIds/deptIds（列表页不带这两个字段；api/role.ts 的 RoleDto 未声明） */
type RoleDetail = RoleDto & { menuIds?: string[] | null; deptIds?: string[] | null }
type RoleQueryModel = { keyword: string; status: number | null }

type RoleFormModel = {
  id: string | null
  roleCode: string
  roleName: string
  dataScope: number
  sort: number
  status: number
  remark: string
  version: number
}

const list = usePageList<RoleRow, RoleQueryModel>({
  fetcher: pageRoles,
  defaultQuery: () => ({ keyword: '', status: null })
})
const user = useUserStore()

/* -------------------------------- 数据源树 -------------------------------- */
const menuTree = ref<MenuTreeNode[]>([])
const deptNodes = ref<TreeOption[]>([])

/** 授权用菜单树：去掉空 children，label 带类型后缀，方便辨认按钮 */
const menuTreeOptions = computed<TreeOption[]>(() =>
  toTreeOptions(
    pruneChildren(menuTree.value),
    (node) => node.id,
    (node) => `${node.menuName}${node.menuType === 3 ? '（按钮）' : node.menuType === 1 ? '（目录）' : ''}`,
    (node) => node.children
  )
)

/** 树数据源失败不静默吞掉——之前吞掉后授权弹窗只剩一棵空树，看不出是 403 */
async function loadTrees(): Promise<void> {
  menuTree.value = (await getGrantMenuTree()) ?? []
  const dept = (await getDeptTree()) ?? []
  deptNodes.value = toTreeOptions(
    pruneChildren(dept),
    (node) => node.id,
    (node) => node.deptName,
    (node) => node.children
  )
}

/* -------------------------------- CRUD 弹窗 -------------------------------- */
const modalVisible = ref(false)
const saving = ref(false)
const editing = computed(() => !!form.id)
const formRef = ref<FormInst | null>(null)

const form = reactive<RoleFormModel>({
  id: null,
  roleCode: '',
  roleName: '',
  dataScope: 4,
  sort: 10,
  status: 0,
  remark: '',
  version: 0
})

const rules: FormRules = {
  roleCode: [
    { required: true, min: 2, max: 64, message: '角色编码 2-64 个字符', trigger: ['input', 'blur'] },
    {
      pattern: /^[A-Za-z][\w.-]*$/,
      message: '角色编码需以字母开头，可含数字/下划线/中划线',
      trigger: ['input', 'blur']
    }
  ],
  roleName: [{ required: true, max: 64, message: '请输入角色名称', trigger: ['input', 'blur'] }]
}

function resetForm(): void {
  form.id = null
  form.roleCode = ''
  form.roleName = ''
  form.dataScope = 4
  form.sort = 10
  form.status = 0
  form.remark = ''
  form.version = 0
}

function openCreate(): void {
  resetForm()
  modalVisible.value = true
}

function openEdit(row: RoleRow): void {
  form.id = row.id
  form.roleCode = row.roleCode
  form.roleName = row.roleName
  form.dataScope = row.dataScope ?? 4
  form.sort = row.sort ?? 0
  form.status = row.status
  form.remark = row.remark ?? ''
  form.version = row.version
  modalVisible.value = true
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  const payload = {
    roleCode: form.roleCode.trim(),
    roleName: form.roleName.trim(),
    dataScope: form.dataScope,
    sort: form.sort,
    status: form.status,
    remark: form.remark.trim() || null
  }
  try {
    if (editing.value && form.id) {
      // 后端 PUT /sys/role/{id}（RoleUpdateDto 含 version）
      await updateRole(form.id, { ...payload, version: form.version })
      message.success('角色已保存')
    } else {
      await createRole(payload)
      message.success('角色已新增')
    }
    await list.load()
    return true
  } catch {
    return false
  } finally {
    saving.value = false
  }
}

async function remove(row: RoleRow): Promise<void> {
  try {
    await deleteRole(row.id)
    message.success('已删除')
    await list.load()
  } catch {
    /* 已提示 */
  }
}

/* ------------------------------- 授权菜单弹窗 ------------------------------- */
const grantVisible = ref(false)
const grantSaving = ref(false)
const grantLoading = ref(false)
const grantRole = ref<RoleRow | null>(null)
const checkedKeys = ref<string[]>([])
const expandedKeys = ref<string[]>([])
const grantModel = reactive({ dataScope: 1, deptIds: [] as string[] })

async function openGrant(row: RoleRow): Promise<void> {
  grantRole.value = row
  grantModel.dataScope = row.dataScope ?? 1
  grantModel.deptIds = []
  checkedKeys.value = []
  expandedKeys.value = []
  grantVisible.value = true
  grantLoading.value = true
  try {
    if (!menuTree.value.length) await loadTrees()
    const detail = (await getRole(row.id)) as RoleDetail
    grantModel.dataScope = detail.dataScope ?? row.dataScope ?? 1
    grantModel.deptIds = [...(detail.deptIds ?? [])]
    checkedKeys.value = [...(detail.menuIds ?? [])]
    expandedKeys.value = flattenIds(menuTree.value)
  } catch {
    /* 已提示 */
  } finally {
    grantLoading.value = false
  }
}

function flattenIds(nodes: MenuTreeNode[]): string[] {
  const out: string[] = []
  nodes.forEach((node) => {
    out.push(node.id)
    if (node.children?.length) out.push(...flattenIds(node.children))
  })
  return out
}

function checkAll(): void {
  checkedKeys.value = flattenIds(menuTree.value)
}

function checkNone(): void {
  checkedKeys.value = []
}

async function submitGrant(): Promise<void> {
  const role = grantRole.value
  if (!role) return
  grantSaving.value = true
  try {
    await grantRoleMenus(role.id, {
      menuIds: toIds(checkedKeys.value),
      dataScope: grantModel.dataScope,
      deptIds: grantModel.dataScope === SCOPE_CUSTOM ? toIds(grantModel.deptIds) : []
    })
    message.success(`「${role.roleName}」授权已保存`)
    grantVisible.value = false
    await list.load()
  } catch {
    /* 已提示 */
  } finally {
    grantSaving.value = false
  }
}

/* ---------------------------------- 表格 ---------------------------------- */
/** 内置 admin 角色：仅超级管理员可见编辑/授权（后端 GuardAdminRole 同步兜底） */
function lockedAsAdmin(row: RoleRow): boolean {
  return row.roleCode === 'admin' && !user.isAdmin
}

const columns = computed<DataTableColumns<RoleRow>>(() => [
  { title: '角色编码', key: 'roleCode', width: 150 },
  { title: '角色名称', key: 'roleName', width: 150 },
  {
    title: '数据权限',
    key: 'dataScope',
    width: 130,
    render: (row) =>
      h(NTag, { size: 'small', bordered: false, type: row.dataScope === SCOPE_CUSTOM ? 'warning' : 'info' }, { default: () => scopeLabel(row.dataScope) })
  },
  { title: '排序', key: 'sort', width: 76 },
  {
    title: '状态',
    key: 'status',
    width: 84,
    render: (row) => {
      const tag = statusTag(row.status)
      return h(NTag, { size: 'small', type: tag.type, bordered: false }, { default: () => tag.label })
    }
  },
  { title: '备注', key: 'remark', minWidth: 160, ellipsis: { tooltip: true }, render: (row) => row.remark ?? '-' },
  { title: '创建时间', key: 'createTime', width: 165, render: (row) => formatDateTime(row.createTime) },
  {
    title: '操作',
    key: 'actions',
    width: 190,
    render: (row) => {
      const builtIn = lockedAsAdmin(row)
      return h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm('sys:role:edit') && !builtIn
            ? h(NButton, { size: 'tiny', text: true, type: 'primary', onClick: () => openEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('sys:role:edit') && !builtIn
            ? h(NButton, { size: 'tiny', text: true, type: 'info', onClick: () => openGrant(row) }, { default: () => '授权菜单' })
            : null,
          hasPerm('sys:role:delete') && !builtIn
            ? h(
                NPopconfirm,
                { onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除角色「${row.roleName}」将同时解除其菜单授权，确定？`
                }
              )
            : null
        ]
      })
    }
  }
])

onMounted(() => {
  // 全局提示已由 http 拦截器负责，这里只防止未处理的 rejection
  void loadTrees().catch(() => undefined)
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
              placeholder="角色编码 / 名称"
              clearable
              style="width: 220px"
              @keyup.enter="list.search"
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

      <NSpace justify="space-between" align="center" style="margin: 16px 0 12px">
        <NButton v-permission="'sys:role:add'" type="primary" @click="openCreate">新增角色</NButton>
        <span class="ps-muted">共 {{ list.total.value }} 条 · 内置 admin 角色不可删改授权</span>
      </NSpace>

      <NDataTable
        remote
        :columns="columns"
        :data="list.data.value"
        :loading="list.loading.value"
        :pagination="list.pagination.value"
        :row-key="(row: RoleRow) => row.id"
        :scroll-x="1080"
        size="small"
        :bordered="false"
        @update:sorter="list.applySorter"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑角色' : '新增角色'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 560px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
      @after-leave="resetForm"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="86">
        <NFormItem label="角色编码" path="roleCode">
          <NInput v-model:value="form.roleCode" :disabled="editing" maxlength="64" placeholder="如 manager" />
        </NFormItem>
        <NFormItem label="角色名称" path="roleName">
          <NInput v-model:value="form.roleName" maxlength="64" placeholder="如 部门主管" />
        </NFormItem>
        <NFormItem label="数据权限" path="dataScope">
          <NSelect v-model:value="form.dataScope" :options="SCOPE_OPTIONS" />
        </NFormItem>
        <NFormItem label="排序" path="sort">
          <NInputNumber v-model:value="form.sort" :min="0" :max="9999" style="width: 140px" />
        </NFormItem>
        <NFormItem label="状态" path="status">
          <NSwitch :value="form.status === 0" @update:value="(v: boolean) => (form.status = v ? 0 : 1)">
            <template #checked>正常</template>
            <template #unchecked>停用</template>
          </NSwitch>
        </NFormItem>
        <NFormItem label="备注" path="remark">
          <NInput v-model:value="form.remark" type="textarea" :rows="2" maxlength="512" show-count />
        </NFormItem>
      </NForm>
    </NModal>

    <NModal
      v-model:show="grantVisible"
      preset="card"
      :title="`授权菜单 · ${grantRole?.roleName ?? ''}`"
      style="width: 720px"
      :bordered="false"
    >
      <NAlert type="info" :bordered="false" style="margin-bottom: 12px">
        勾选父节点自动级联子节点；保存后该角色成员的菜单与按钮权限即时刷新（后端权限缓存已失效）。
      </NAlert>
      <NGrid cols="2" :x-gap="16">
        <NGi>
          <NForm label-placement="top" :show-feedback="false">
            <NFormItem label="数据权限（五档）">
              <NSelect v-model:value="grantModel.dataScope" :options="SCOPE_OPTIONS" />
            </NFormItem>
            <NFormItem v-if="grantModel.dataScope === SCOPE_CUSTOM" label="自定义数据部门">
              <NTreeSelect
                :value="grantModel.deptIds"
                multiple
                cascade
                :options="deptNodes"
                placeholder="请选择可见部门（可多选）"
                clearable
                filterable
                max-tag-count="responsive"
                style="width: 100%"
                @update:value="grantModel.deptIds = toStrIds($event)"
              />
            </NFormItem>
            <NFormItem label="已勾选菜单">
              <NSpace :size="8" align="center">
                <NButton size="tiny" tertiary @click="checkAll">全选</NButton>
                <NButton size="tiny" tertiary @click="checkNone">清空</NButton>
                <span class="ps-muted">{{ checkedKeys.length }} 项</span>
              </NSpace>
            </NFormItem>
          </NForm>
        </NGi>
        <NGi>
          <div class="ps-grant-tree">
            <NTree
              v-if="!grantLoading && menuTreeOptions.length"
              block-line
              checkable
              :cascade="true"
              :data="menuTreeOptions"
              :checked-keys="checkedKeys"
              v-model:expanded-keys="expandedKeys"
              @update:checked-keys="checkedKeys = toStrIds($event)"
            />
            <NAlert v-else-if="!grantLoading" type="warning" :bordered="false">
              菜单树为空或加载失败，请关闭本弹窗后重新打开。
            </NAlert>
            <NAlert v-else type="default" :bordered="false">菜单树加载中…</NAlert>
          </div>
        </NGi>
      </NGrid>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="grantVisible = false">取消</NButton>
          <NButton type="primary" :loading="grantSaving" @click="submitGrant">保存授权</NButton>
        </NSpace>
      </template>
    </NModal>
  </div>
</template>

<style scoped>
.ps-grant-tree {
  max-height: 320px;
  overflow-y: auto;
  border: 1px solid rgba(100, 108, 136, 0.16);
  border-radius: 8px;
  padding: 6px;
}
</style>
