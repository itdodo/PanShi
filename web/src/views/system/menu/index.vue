<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import {
  NAlert,
  NButton,
  NCard,
  NDataTable,
  NForm,
  NFormItem,
  NInput,
  NInputNumber,
  NModal,
  NPopconfirm,
  NRadio,
  NRadioGroup,
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
import { put } from '@/api/http'
import { createMenu, deleteMenu, getFullMenuTree, MENU_TYPE, type MenuFormDto, type MenuTreeNode, type MenuTypeValue } from '@/api/menu'
import { hasPerm } from '@/directives/permission'
import { message } from '@/utils/feedback'
import { flattenTree, menuTypeTag, pruneChildren, statusTag, toId, toTreeOptions } from '../_shared'
import AppIcon from '@/components/AppIcon.vue'

/**
 * 菜单管理（/sys/menu）：树形表格 + 按 menuType 动态显隐的编辑弹窗 + 删除（上移/下移本批次不做）。
 * 后端 GET /sys/menu/tree（全量树，需 sys:menu:list）、POST /sys/menu、PUT /sys/menu/{id}、DELETE /sys/menu/{id}。
 * ⚠️ 乐观锁：后端 MenuDto 目前不回传 version（新增落库 version=0），故页内用 versionTracker 记「本会话已知版本」，
 *    每次编辑成功后 +1；他人并发改动由 409 拦截器提示，刷新页面即回到真值。
 */
type MenuFormModel = {
  id: string | null
  parentId: string
  menuName: string
  menuType: MenuTypeValue
  path: string
  component: string
  permission: string
  icon: string
  visible: boolean
  status: number
  sort: number
}

/** 已确认存在的菜单版本表（见文件头说明） */
const versionTracker = new Map<string, number>()

/**
 * 后端幂等哨兵菜单（权限码 __seed_v1__，DbSeeder 里那行 Btn(9999,...,"SEED_V1")）——
 * 它只是「是否已播种」的标记，非真实菜单，却在 /sys/menu/tree 里混成一条脏行、还可能被误删导致重复播种。
 * 故从管理树中剔除（侧栏本就因 visible=false+按钮型不显示它，不受影响）。
 */
const SEED_SENTINEL_PERM = '__seed_v1__'
function stripSentinel(nodes: MenuTreeNode[]): MenuTreeNode[] {
  return nodes
    .filter((node) => node.permission !== SEED_SENTINEL_PERM)
    .map((node) => (node.children?.length ? { ...node, children: stripSentinel(node.children) } : node))
}

/** 默认只展开到「页面」层：展开含目录/页面子节点的节点，收起各页面下的按钮，避免满屏按钮显乱 */
function defaultExpandedKeys(nodes: MenuTreeNode[]): string[] {
  const out: string[] = []
  const walk = (list: MenuTreeNode[]): void => {
    list.forEach((node) => {
      const kids = node.children ?? []
      if (kids.some((kid) => kid.menuType !== MENU_TYPE.Button)) out.push(node.id)
      walk(kids)
    })
  }
  walk(nodes)
  return out
}

const treeData = ref<MenuTreeNode[]>([])
const loading = ref(false)
const keyword = ref('')
const expandedKeys = ref<DataTableRowKey[]>([])

const flatRows = computed<MenuTreeNode[]>(() => flattenTree(treeData.value, (node) => node.children))

const tableData = computed<MenuTreeNode[]>(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return pruneChildren(treeData.value)
  return flatRows.value.filter((row) =>
    [row.menuName, row.path, row.component, row.permission, row.icon]
      .filter(Boolean)
      .some((field) => String(field).toLowerCase().includes(kw))
  )
})

async function loadTree(): Promise<void> {
  loading.value = true
  try {
    const nodes = await getFullMenuTree()
    treeData.value = stripSentinel(nodes ?? [])
    expandedKeys.value = defaultExpandedKeys(treeData.value)
  } catch {
    treeData.value = []
  } finally {
    loading.value = false
  }
}

function expandAll(): void {
  expandedKeys.value = flatRows.value.map((row) => row.id)
}

function collapseAll(): void {
  expandedKeys.value = []
}

/* -------------------------------- 父级选择 -------------------------------- */
const ROOT_KEY = '0'

/** 目录/菜单才可作为父级；编辑时排除自身及其子孙（后端只挡「父级=自己」） */
function parentOptions(nodes: MenuTreeNode[], excludeId?: string | null): TreeOption[] {
  return toTreeOptions(
    nodes.filter((node) => node.menuType !== MENU_TYPE.Button && node.id !== excludeId),
    (node) => node.id,
    (node) => node.menuName,
    (node) => node.children
  )
}

const parentSelectOptions = computed<TreeOption[]>(() => [
  { key: ROOT_KEY, label: '顶级（无父级）' },
  ...parentOptions(treeData.value, form.id)
])

/* -------------------------------- 编辑弹窗 -------------------------------- */
const modalVisible = ref(false)
const saving = ref(false)
const editing = computed(() => !!form.id)
const isButton = computed(() => form.menuType === MENU_TYPE.Button)
const isDirectory = computed(() => form.menuType === MENU_TYPE.Directory)
const isMenu = computed(() => form.menuType === MENU_TYPE.Menu)
const formRef = ref<FormInst | null>(null)

const form = reactive<MenuFormModel>({
  id: null,
  parentId: ROOT_KEY,
  menuName: '',
  menuType: MENU_TYPE.Menu,
  path: '',
  component: '',
  permission: '',
  icon: '',
  visible: true,
  status: 0,
  sort: 10
})

const rules = computed<FormRules>(() => ({
  menuName: [{ required: true, max: 64, message: '请输入菜单名称', trigger: ['input', 'blur'] }],
  path: isButton.value
    ? []
    : [{ required: true, max: 256, message: '目录/菜单需填写路由路径（目录可填 /xxx）', trigger: ['input', 'blur'] }],
  component: form.menuType === MENU_TYPE.Menu
    ? [{ required: true, max: 256, message: '菜单需填写组件路径（views 相对路径，如 system/menu/index）', trigger: ['input', 'blur'] }]
    : [],
  permission: isButton.value
    ? [{ required: true, max: 128, message: '按钮必须填权限码，如 sys:user:add', trigger: ['input', 'blur'] }]
    : []
}))

function resetForm(): void {
  form.id = null
  form.parentId = ROOT_KEY
  form.menuName = ''
  form.menuType = MENU_TYPE.Menu
  form.path = ''
  form.component = ''
  form.permission = ''
  form.icon = ''
  form.visible = true
  form.status = 0
  form.sort = 10
}

function openCreate(parent?: MenuTreeNode): void {
  resetForm()
  if (parent) {
    form.parentId = parent.id
    form.menuType = parent.menuType === MENU_TYPE.Directory ? MENU_TYPE.Menu : MENU_TYPE.Button
    form.visible = form.menuType !== MENU_TYPE.Button
  }
  modalVisible.value = true
}

function openEdit(row: MenuTreeNode): void {
  form.id = row.id
  form.parentId = row.parentId ?? ROOT_KEY
  form.menuName = row.menuName
  form.menuType = row.menuType
  form.path = row.path ?? ''
  form.component = row.component ?? ''
  form.permission = row.permission ?? ''
  form.icon = row.icon ?? ''
  form.visible = row.visible
  form.status = row.status
  form.sort = row.sort
  modalVisible.value = true
}

function buildDto(): MenuFormDto {
  return {
    parentId: form.parentId === ROOT_KEY ? null : form.parentId,
    menuName: form.menuName.trim(),
    menuType: form.menuType,
    path: isButton.value ? null : form.path.trim() || null,
    component: form.menuType === MENU_TYPE.Menu ? form.component.trim() || null : null,
    permission: form.permission.trim() || null,
    icon: isButton.value ? null : form.icon.trim() || null,
    visible: isButton.value ? false : form.visible,
    status: form.status,
    sort: form.sort
  }
}

async function submit(): Promise<boolean> {
  const invalid = await formRef.value?.validate().then(() => false).catch(() => true)
  if (invalid) return false
  saving.value = true
  const dto = buildDto()
  try {
    if (editing.value && form.id) {
      const expected = versionTracker.get(form.id) ?? 0
      // 后端 PUT /sys/menu/{id}（声明式 updateMenu 地址少一段 id，故直调）
      await put(`/sys/menu/${form.id}`, { ...dto, version: expected })
      versionTracker.set(form.id, expected + 1)
      message.success('菜单已保存')
    } else {
      await createMenu(dto)
      message.success('菜单已新增')
    }
    await loadTree()
    return true
  } catch {
    return false
  } finally {
    saving.value = false
  }
}

async function remove(row: MenuTreeNode): Promise<void> {
  try {
    await deleteMenu(row.id)
    versionTracker.delete(row.id)
    message.success('已删除')
    await loadTree()
  } catch {
    /* 已提示 */
  }
}

/* ---------------------------------- 表格 ---------------------------------- */
const columns = computed<DataTableColumns<MenuTreeNode>>(() => [
  {
    title: '菜单名称',
    key: 'menuName',
    minWidth: 210,
    render: (row) =>
      h('div', { class: 'ps-row' }, [
        row.menuType !== MENU_TYPE.Button ? h(AppIcon, { name: row.icon, size: 16 }) : null,
        h('span', row.menuName)
      ])
  },
  {
    title: '类型',
    key: 'menuType',
    width: 78,
    render: (row) => {
      const tag = menuTypeTag(row.menuType)
      return h(NTag, { size: 'small', bordered: false, type: tag.type }, { default: () => tag.label })
    }
  },
  { title: '路由路径', key: 'path', minWidth: 150, render: (row) => row.path ?? '-' },
  { title: '组件路径', key: 'component', minWidth: 170, ellipsis: { tooltip: true }, render: (row) => row.component ?? '-' },
  { title: '权限码', key: 'permission', minWidth: 150, ellipsis: { tooltip: true }, render: (row) => row.permission ?? '-' },
  { title: '排序', key: 'sort', width: 70 },
  {
    title: '显示',
    key: 'visible',
    width: 76,
    render: (row) =>
      h(NTag, { size: 'small', bordered: false, type: row.visible ? 'success' : 'default' }, { default: () => (row.visible ? '显示' : '隐藏') })
  },
  {
    title: '状态',
    key: 'status',
    width: 76,
    render: (row) => {
      const tag = statusTag(row.status)
      return h(NTag, { size: 'small', bordered: false, type: tag.type }, { default: () => tag.label })
    }
  },
  {
    title: '操作',
    key: 'actions',
    width: 190,
    render: (row) =>
      h(NSpace, { size: 10 }, {
        default: () => [
          hasPerm('sys:menu:add') && row.menuType !== MENU_TYPE.Button
            ? h(
                NButton,
                { size: 'tiny', text: true, type: 'primary', onClick: () => openCreate(row) },
                { default: () => '加下级' }
              )
            : null,
          hasPerm('sys:menu:edit')
            ? h(NButton, { size: 'tiny', text: true, type: 'info', onClick: () => openEdit(row) }, { default: () => '编辑' })
            : null,
          hasPerm('sys:menu:delete')
            ? h(
                NPopconfirm,
                { onPositiveClick: () => remove(row) },
                {
                  trigger: () => h(NButton, { size: 'tiny', text: true, type: 'error' }, { default: () => '删除' }),
                  default: () => `删除「${row.menuName}」？存在子菜单时后端会拒绝。`
                }
              )
            : null
        ]
      })
  }
])

onMounted(() => {
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
            placeholder="按名称 / 路径 / 权限码过滤"
            clearable
            style="width: 260px"
          />
          <NButton tertiary @click="loadTree">刷新</NButton>
          <NButton tertiary :disabled="!!keyword.trim()" @click="expandAll">展开全部</NButton>
          <NButton tertiary :disabled="!!keyword.trim()" @click="collapseAll">折叠全部</NButton>
        </NSpace>
        <NSpace :size="8">
          <NButton v-permission="'sys:menu:add'" type="primary" @click="openCreate()">新增菜单</NButton>
        </NSpace>
      </NSpace>

      <NAlert v-if="keyword.trim()" type="info" :bordered="false" style="margin-top: 12px">
        过滤中：命中 {{ tableData.length }} 项（关键字过滤为平铺显示，清空后恢复树形）。
      </NAlert>

      <NDataTable
        class="ps-menu-table"
        :columns="columns"
        :data="tableData"
        :loading="loading"
        :row-key="(row: MenuTreeNode) => row.id"
        v-model:expanded-row-keys="expandedKeys"
        children-key="children"
        size="small"
        :bordered="false"
        :pagination="false"
        :scroll-x="1180"
      />
    </NCard>

    <NModal
      v-model:show="modalVisible"
      preset="dialog"
      :title="editing ? '编辑菜单' : '新增菜单'"
      :positive-text="editing ? '保存' : '创建'"
      negative-text="取消"
      style="width: 640px"
      :positive-button-props="{ loading: saving }"
      @positive-click="submit"
      @after-leave="resetForm"
    >
      <NForm ref="formRef" :model="form" :rules="rules" label-placement="left" label-width="88">
        <NFormItem label="菜单类型" path="menuType">
          <NRadioGroup v-model:value="form.menuType">
            <NSpace :size="16">
              <NRadio :value="MENU_TYPE.Directory" label="目录" />
              <NRadio :value="MENU_TYPE.Menu" label="菜单" />
              <NRadio :value="MENU_TYPE.Button" label="按钮" />
            </NSpace>
          </NRadioGroup>
        </NFormItem>
        <NFormItem label="父级" path="parentId">
          <NTreeSelect
            :value="form.parentId"
            :options="parentSelectOptions"
            placeholder="请选择父级"
            filterable
            style="width: 100%"
            @update:value="form.parentId = toId($event) ?? '0'"
          />
        </NFormItem>
        <NFormItem label="菜单名称" path="menuName">
          <NInput v-model:value="form.menuName" maxlength="64" placeholder="侧边栏显示名" />
        </NFormItem>

        <template v-if="!isButton">
          <NFormItem label="图标" path="icon">
            <NSpace :size="8" align="center" style="width: 100%">
              <NInput v-model:value="form.icon" maxlength="128" placeholder="iconify 名，如 lucide:users" style="width: 300px" />
              <AppIcon :name="form.icon" :size="20" />
            </NSpace>
          </NFormItem>
          <NFormItem label="路由路径" path="path">
            <NInput v-model:value="form.path" maxlength="256" :placeholder="isDirectory ? '目录可填 /system' : '如 /system/menu'" />
          </NFormItem>
        </template>

        <NFormItem v-if="isMenu" label="组件路径" path="component">
          <NInput v-model:value="form.component" maxlength="256" placeholder="views 相对路径，如 system/menu/index" />
        </NFormItem>

        <NFormItem v-if="isButton" label="权限码" path="permission">
          <NInput v-model:value="form.permission" maxlength="128" placeholder="必填，如 sys:user:add" />
        </NFormItem>

        <NFormItem label="排序" path="sort">
          <NInputNumber v-model:value="form.sort" :min="0" :max="9999" style="width: 140px" />
        </NFormItem>
        <NFormItem v-if="!isButton" label="显示" path="visible">
          <NSwitch v-model:value="form.visible" />
        </NFormItem>
        <NFormItem label="状态" path="status">
          <NSwitch :value="form.status === 0" @update:value="(v: boolean) => (form.status = v ? 0 : 1)">
            <template #checked>启用</template>
            <template #unchecked>停用</template>
          </NSwitch>
        </NFormItem>
      </NForm>
    </NModal>
  </div>
</template>

<style scoped>
.ps-menu-table {
  margin-top: 14px;
}
</style>
