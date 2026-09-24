<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import {
  NButton,
  NCard,
  NDataTable,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NModal,
  NPopconfirm,
  NSelect,
  NSpace,
  NSwitch,
  NTag,
  NTreeSelect,
  type DataTableColumns,
  type DataTableRowKey,
  type FormInst,
  type FormRules,
  type TreeOption
} from 'naive-ui'
import { get, post, put } from '@/api/http'
import { deleteDept } from '@/api/system/dept'
import { userOptions, type Option } from '@/api/admin'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { flattenTree, pruneChildren, statusTag, toId, toTreeOptions } from '../_shared'

/**
 * 部门管理（/sys/dept）：树形表格 + CRUD（上级 NTreeSelect、负责人取 /sys/user/options）。
 * 后端返回 DeptDto：{id,parentId,deptCode,deptName,leader,leaderUserId,sort,status,children[],createTime,version}，
 * 编辑 PUT /sys/dept/{id}（DeptSaveDto 含 version）；新增 POST /sys/dept；删除 DELETE /sys/dept/{id}。
 */
type DeptRow = {
  id: string
  parentId: string | null
  deptCode: string
  deptName: string
  leader?: string | null
  leaderUserId?: string | null
  sort: number
  status: number
  createTime?: string | null
  version: number
  children?: DeptRow[]
}

type DeptFormModel = {
  id: string | null
  parentId: string
  deptCode: string
  deptName: string
  leaderUserId: string | null
  sort: number
  status: number
  version: number
}

const ROOT_KEY = '0'

const treeData = ref<DeptRow[]>([])
const loading = ref(false)
const keyword = ref('')
const expandedKeys = ref<DataTableRowKey[]>([])
const users = ref<Option[]>([])

const userMap = computed(() => new Map(users.value.map((item) => [item.value, item.label])))
const userSelectOptions = computed(() => users.value.map((item) => ({ label: item.label, value: item.value })))

const flatRows = computed<DeptRow[]>(() => flattenTree(treeData.value, (node) => node.children))

/** 关键字由后端 /sys/dept/tree?keyword= 过滤（保留层级），前端不再二次过滤 */
const tableData = computed<DeptRow[]>(() => pruneChildren(treeData.value))

/** 上级候选：排除自身及子孙（防成环） */
const parentOptions = computed<TreeOption[]>(() => {
  const exclude = new Set<string>(form.id ? collectSubtreeIds(treeData.value, form.id) : [])
  const roots: TreeOption[] = [{ key: ROOT_KEY, label: '顶级部门' }]
  return roots.concat(
    toTreeOptions(
      treeData.value.filter((node) => !exclude.has(node.id)),
      (node) => node.id,
      (node) => node.deptName,
      (node) => node.children?.filter((child) => !exclude.has(child.id))
    )
  )
})

function collectSubtreeIds(nodes: DeptRow[], id: string): string[] {
  const out: string[] = []
  const walk = (list: DeptRow[], inside: boolean): void => {
    list.forEach((node) => {
      const hit = inside || node.id === id
      if (hit) out.push(node.id)
      if (node.children?.length) walk(node.children, hit)
    })
  }
  walk(nodes, false)
  return out
}

async function loadTree(): Promise<void> {
  loading.value = true
  try {
    const kw = keyword.value.trim()
    treeData.value = (await get<DeptRow[]>('/sys/dept/tree', kw ? { keyword: kw } : undefined)) ?? []
    expandedKeys.value = flattenTree(treeData.value, (node) => node.children).map((node) => node.id)
  } catch {
    treeData.value = []
  } finally {
    loading.value = false
  }
}

/* -------------------------------- 编辑弹窗 -------------------------------- */
const modalVisible = ref(false)
const saving = ref(false)
const editing = computed(() => !!form.id)
const formRef = ref<FormInst | null>(null)

const form = reactive<DeptFormModel>({
  id: null,
  parentId: ROOT_KEY,
  deptCode: '',
  deptName: '',
  leaderUserId: null,
  sort: 10,
  status: 0,
  version: 0
})

const rules: FormRules = {
  deptCode: [
    { required: true, max: 64, message: '请输入部门编码', trigger: ['input', 'blur'] },
    { pattern: /^[A-Za-z][\w.-]*$/, message: '部门编码需以字母开头', trigger: ['input', 'blur'] }
  ],
  deptName: [{ required: true, max: 64, message: '请输入部门名称', trigger: ['input', 'blur'] }]
}

function resetForm(): void {
  form.id = null
  form.parentId = ROOT_KEY
  form.deptCode = ''
  form.deptName = ''
  form.leaderUserId = null
  form.sort = 10
  form.status = 0
  form.version = 0
}

function openCreate(parent?: DeptRow): void {
  resetForm()
  if (parent) form.parentId = parent.id
  modalVisible.value = true
}

function openEdit(row: DeptRow): void {
  form.id = row.id
  form.parentId = row.parentId ?? ROOT_KEY
  form.deptCode = row.deptCode
  form.deptName = row.deptName
  form.leaderUserId = row.leaderUserId ?? null
  form.sort = row.sort
  form.status = row.status
  form.version = row.version
  modalVisible.value = true
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  const payload = {
    parentId: form.parentId === ROOT_KEY ? null : form.parentId,
    deptCode: form.deptCode.trim(),
    deptName: form.deptName.trim(),
    leaderUserId: form.leaderUserId,
    sort: form.sort,
    status: form.status
  }
  try {
    if (editing.value && form.id) {
      await put(`/sys/dept/${form.id}`, { ...payload, version: form.version })
      message.success('部门已保存')
    } else {
      await post('/sys/dept', payload)
      message.success('部门已新增')
    }
    await loadTree()
    return true
  } catch {
    return false
  } finally {
    saving.value = false
  }
}

async function remove(row: DeptRow): Promise<void> {
  try {
    await deleteDept(row.id)
    message.success('已删除')
    await loadTree()
  } catch {
    /* 已提示 */
  }
}

/* ---------------------------------- 表格 ---------------------------------- */
const columns = computed<DataTableColumns<DeptRow>>(() => [
  { title: '部门名称', key: 'deptName', minWidth: 200 },
  { title: '部门编码', key: 'deptCode', width: 150 },
  {
    title: '负责人',
    key: 'leaderUserId',
    width: 170,
    render: (row) =>
      row.leaderUserId
        ? h('span', userMap.value.get(row.leaderUserId) ?? row.leader ?? row.leaderUserId)
        : h('span', { class: 'ps-muted' }, row.leader ?? '未指定')
  },
  { title: '排序', key: 'sort', width: 76 },
  {
    title: '状态',
    key: 'status',
    width: 84,
    render: (row) => {
      const tag = statusTag(row.status)
      return h(NTag, { size: 'small', bordered: false, type: tag.type }, { default: () => tag.label })
    }
  },
  {
    title: '操作',
    key: 'actions',
    width: 180,
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm('sys:dept:add')
            ? h(NButton, { size: 'tiny', text: true, type: 'primary', onClick: () => openCreate(row) }, { default: () => '加下级' })
            : null,
          hasPerm('sys:dept:edit')
            ? h(NButton, { size: 'tiny', text: true, type: 'info', onClick: () => openEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('sys:dept:delete')
            ? h(
                NPopconfirm,
                { onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除部门「${row.deptName}」？有子部门或已挂用户时后端会拒绝。`
                }
              )
            : null
        ]
      })
  }
])

onMounted(() => {
  userOptions()
    .then((rows) => (users.value = rows ?? []))
    .catch(() => (users.value = []))
  void loadTree()
})
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NSpace justify="space-between" align="center" wrap :size="12">
        <NSpace :size="12" align="center" wrap>
          <NInput
            v-model:value="keyword"
            placeholder="按名称 / 编码过滤（后端过滤，保留层级）"
            clearable
            style="width: 300px"
            @keyup.enter="loadTree"
            @clear="loadTree"
          />
          <NButton type="primary" @click="loadTree">查询</NButton>
        </NSpace>
        <NButton v-permission="'sys:dept:add'" type="primary" @click="openCreate()">新增部门</NButton>
      </NSpace>

      <NDataTable
        class="ps-dept-table ps-tree-table"
        :columns="columns"
        :data="tableData"
        :loading="loading"
        :row-key="(row: DeptRow) => row.id"
        v-model:expanded-row-keys="expandedKeys"
        children-key="children"
        size="small"
        :bordered="false"
        :pagination="false"
        :scroll-x="900"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑部门' : '新增部门'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 560px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
      @after-leave="resetForm"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="86">
        <NFormItem label="上级部门" path="parentId">
          <NTreeSelect
            :value="form.parentId"
            :options="parentOptions"
            placeholder="请选择上级部门"
            filterable
            style="width: 100%"
            @update:value="form.parentId = toId($event) ?? '0'"
          />
        </NFormItem>
        <NFormItem label="部门编码" path="deptCode">
          <NInput v-model:value="form.deptCode" :disabled="editing" maxlength="64" placeholder="如 tech、tech.fe" />
        </NFormItem>
        <NFormItem label="部门名称" path="deptName">
          <NInput v-model:value="form.deptName" maxlength="64" placeholder="如 研发部" />
        </NFormItem>
        <NFormItem label="负责人" path="leaderUserId">
          <NSelect
            v-model:value="form.leaderUserId"
            :options="userSelectOptions"
            placeholder="可选，取用户下拉"
            clearable
            filterable
            max-tag-count="responsive"
          />
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
      </NForm>
    </NModal>
  </div>
</template>

<style scoped>
.ps-dept-table {
  margin-top: 14px;
}
</style>
