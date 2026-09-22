<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import {
  NAlert,
  NButton,
  NCard,
  NCode,
  NCollapse,
  NCollapseItem,
  NDivider,
  NDrawer,
  NDrawerContent,
  NForm,
  NFormItem,
  NGrid,
  NFormItemGi,
  NInput,
  NInputNumber,
  NPopconfirm,
  NScrollbar,
  NSelect,
  NSpace,
  NSwitch,
  NTag,
  type DataTableColumns,
  type SelectOption
} from 'naive-ui'
import {
  createFlowDef,
  deleteFlowDef,
  enableFlowDef,
  flowCategories,
  pageFlowDefs,
  updateFlowDef,
  type FlowDefDto
} from '@/api/flow'
import { pagePositions, positionOptions, roleOptions, userOptions, type Option } from '@/api/admin'
import { pageRoles } from '@/api/system/role'
import { usePageList } from '@/composables/usePageList'
import { hasPerm } from '@/directives/permission'
import { formatDateTime } from '@/utils/format'
import { message } from '@/utils/feedback'
import { errorText } from '@/api/types'
import {
  cleanGraph,
  parseGraph,
  stringifyGraph,
  type FlowApproverDto,
  type FlowGraphDto,
  type FlowNodeDto
} from '@/components/flowGraph'
import { nodeTypeLabel } from '@/components/flowEnums'

/**
 * 流程定义 + 简版流程设计器（节点顺序列表编辑器，不做拖拽）。
 * 保存 = createFlowDef（新流程，编码后端自增）/ updateFlowDef（同编码新版本，落库即停用）；
 * nodeJson 为 FlowGraph DSL 的 stringify 结果，结构合法性由后端 FlowGraphValidator 校验并回 msg。
 */

/* ------------------------------- 设计器数据模型 ------------------------------- */
interface DraftCondition {
  variable: string
  op: string
  value: string
}

interface DraftBranch {
  name: string
  priority: number
  next: string
  conditions: DraftCondition[]
}

interface DraftApprover {
  /** user/role/position/deptLeader/submitterChoice */
  type: string
  userIds: string[]
  /** DSL 存 roleCode / positionCode（后端按编码解析），选项 value 即编码 */
  codes: string[]
  scope: string
  deptId: number | null
}

interface DraftNode {
  key: number
  code: string
  type: string
  name: string
  mode: string
  next: string
  defaultNext: string
  approvers: DraftApprover[]
  branches: DraftBranch[]
  ccUserIds: string[]
}

const TYPE_CHOICES: SelectOption[] = [
  { label: '审批节点', value: 'approval' },
  { label: '条件分支', value: 'condition' },
  { label: '抄送节点', value: 'cc' }
]

const APPROVER_CHOICES: SelectOption[] = [
  { label: '指定人员', value: 'user' },
  { label: '按角色', value: 'role' },
  { label: '按岗位', value: 'position' },
  { label: '部门主管', value: 'deptLeader' },
  { label: '发起人自选', value: 'submitterChoice' }
]

const SCOPE_CHOICES: SelectOption[] = [
  { label: '全公司', value: 'company' },
  { label: '仅发起人部门', value: 'submitterDept' }
]

const OP_CHOICES: SelectOption[] = [
  { label: '小于 lt', value: 'lt' },
  { label: '小于等于 le', value: 'le' },
  { label: '大于 gt', value: 'gt' },
  { label: '大于等于 ge', value: 'ge' },
  { label: '等于 eq', value: 'eq' },
  { label: '不等于 ne', value: 'ne' },
  { label: '属于 in', value: 'in' },
  { label: '包含 contains', value: 'contains' }
]

const STATUS_CHOICES: SelectOption[] = [
  { label: '全部', value: 0 },
  { label: '启用', value: 1 },
  { label: '停用', value: 2 }
]

let keySeed = 1
const nextKey = (): number => keySeed++
const shortCode = (): string => `n${Date.now().toString(36).slice(-4)}${Math.floor(Math.random() * 90 + 10)}`

/* ---------------------------------- 列表区 ---------------------------------- */
const list = usePageList<FlowDefDto, { keyword: string; category: string; status: number | null }>({
  fetcher: pageFlowDefs,
  defaultQuery: () => ({ keyword: '', category: '', status: null })
})
const { queryParams, loading, data, pagination, search, reset, load } = list

const categories = ref<SelectOption[]>([])

async function loadCategories(): Promise<void> {
  try {
    const codes = await flowCategories()
    categories.value = (codes ?? []).filter(Boolean).map((c) => ({ label: c, value: c }))
  } catch {
    categories.value = []
  }
}

/** 下拉用 0/1/2 表达（NSelect 不接受 null）：0 全部 → null、1 启用 → 1、2 停用 → 0 */
function pickStatus(raw: unknown): void {
  const value = Number(raw ?? 0)
  queryParams.status = value === 0 ? null : value === 1 ? 1 : 0
  void search()
}

const statusFilterValue = computed<number>(() =>
  queryParams.status === null ? 0 : queryParams.status === 1 ? 1 : 2
)

async function toggleStatus(row: FlowDefDto, status: number): Promise<void> {
  try {
    await enableFlowDef(row.id, status)
    message.success(status === 1 ? `已启用 ${row.flowName} v${row.flowVersion}` : '已停用')
  } catch {
    /* 拦截器已提示（DSL 非法/同编码切换） */
  } finally {
    await load()
  }
}

/* --------------------------------- 设计器状态 --------------------------------- */
const designer = reactive({
  show: false,
  mode: 'create' as 'create' | 'edit',
  id: '',
  flowCode: '',
  baseVersion: 0,
  flowName: '',
  category: '',
  remark: '',
  version: 0
})
const nodes = ref<DraftNode[]>([])
const saving = ref(false)
const previewExpanded = ref<string[]>([])
const newVersionTip = ref(false)

const userOpts = ref<SelectOption[]>([])
const roleOpts = ref<SelectOption[]>([])
const positionOpts = ref<SelectOption[]>([])

/**
 * 角色/岗位选项：DSL 里存的是编码（后端 ResolveApprovers 按 RoleCode/PositionCode 查），
 * 故优先用分页接口取 code→name；无 sys:*:list 权限时回落通用 options（value 为 id）。
 */
async function loadChoices(): Promise<void> {
  try {
    userOpts.value = (await userOptions()).map((o: Option) => ({ label: o.label, value: o.value }))
  } catch {
    userOpts.value = []
  }
  try {
    const roles = await pageRoles({ pageNum: 1, pageSize: 200 })
    roleOpts.value = (roles?.rows ?? []).map((r) => ({ label: `${r.roleName}（${r.roleCode}）`, value: r.roleCode }))
  } catch {
    roleOpts.value = (await safeOptions(roleOptions))
  }
  try {
    const positions = await pagePositions({ pageNum: 1, pageSize: 200 })
    positionOpts.value = (positions?.rows ?? []).map((p) => ({ label: `${p.positionName}（${p.positionCode}）`, value: p.positionCode }))
  } catch {
    positionOpts.value = (await safeOptions(positionOptions))
  }
}

async function safeOptions(loader: () => Promise<Option[]>): Promise<SelectOption[]> {
  try {
    return (await loader()).map((o) => ({ label: o.label, value: o.value }))
  } catch {
    return []
  }
}

function newApprover(): DraftApprover {
  return { type: 'user', userIds: [], codes: [], scope: 'company', deptId: null }
}

/** 新流程骨架：start → 审批 → end，用户在此之上加节点 */
function defaultDraft(): DraftNode[] {
  return [
    {
      key: nextKey(),
      code: 'start',
      type: 'start',
      name: '发起',
      mode: '',
      next: 'n1',
      defaultNext: '',
      approvers: [],
      branches: [],
      ccUserIds: []
    },
    {
      key: nextKey(),
      code: 'n1',
      type: 'approval',
      name: '主管审批',
      mode: 'orSign',
      next: 'end',
      defaultNext: '',
      approvers: [newApprover()],
      branches: [],
      ccUserIds: []
    },
    {
      key: nextKey(),
      code: 'end',
      type: 'end',
      name: '结束',
      mode: '',
      next: '',
      defaultNext: '',
      approvers: [],
      branches: [],
      ccUserIds: []
    }
  ]
}

function newBranch(code: string): DraftBranch {
  return { name: '新分支', priority: nodes.value.flatMap((n) => n.branches).length + 1, next: code || 'end', conditions: [{ variable: 'amount', op: 'lt', value: '10000' }] }
}

/* ------------------------------ DSL ⇄ 草稿互转 ------------------------------ */
function graphToDraft(nodeJson: string): DraftNode[] {
  const graph = parseGraph(nodeJson)
  const draft = graph.nodes.map<DraftNode>((n) => ({
    key: nextKey(),
    code: n.code,
    type: n.type,
    name: n.name ?? '',
    mode: n.mode ?? (n.type === 'approval' ? 'orSign' : ''),
    next: n.next ?? '',
    defaultNext: n.defaultNext ?? '',
    approvers: (n.approvers ?? []).map(toDraftApprover),
    branches: (n.branches ?? []).map((b) => ({
      name: b.name ?? '',
      priority: Number(b.priority ?? 100),
      next: b.next ?? '',
      conditions: (b.conditions ?? []).map((c) => ({
        variable: c.variable ?? '',
        op: c.op ?? 'eq',
        value: Array.isArray(c.value) ? c.value.join(', ') : String(c.value ?? '')
      }))
    })),
    ccUserIds: (n.ccUserIds ?? []).map(String)
  }))
  return draft.length ? draft : defaultDraft()
}

function toDraftApprover(a: FlowApproverDto): DraftApprover {
  return {
    type: a.type || 'user',
    userIds: (a.userIds ?? []).map(String),
    codes: (a.roleCodes ?? a.positionCodes ?? []).map(String),
    scope: a.scope ?? 'company',
    deptId: a.deptId ?? null
  }
}

function draftToGraph(): FlowGraphDto {
  return {
    entry: nodes.value.find((n) => n.type === 'start')?.code ?? 'start',
    nodes: nodes.value.map((n): FlowNodeDto => {
      const node: FlowNodeDto = { code: n.code.trim(), type: n.type, name: n.name.trim() || null }
      if (n.next) node.next = n.next
      if (n.type === 'approval') {
        node.mode = n.mode || 'orSign'
        node.approvers = n.approvers.map(toApproverDto)
      }
      if (n.type === 'condition') {
        node.branches = n.branches.map((b) => ({
          name: b.name,
          priority: Number(b.priority ?? 0) || 0,
          next: b.next,
          conditions: b.conditions
            .filter((c) => c.variable.trim())
            .map((c) => ({ variable: c.variable.trim(), op: c.op, value: conditionValue(c) }))
        }))
        if (n.defaultNext) node.defaultNext = n.defaultNext
      }
      if (n.type === 'cc') node.ccUserIds = n.ccUserIds.filter(Boolean)
      return node
    })
  }
}

function toApproverDto(a: DraftApprover): FlowApproverDto {
  const dto: FlowApproverDto = { type: a.type }
  if (a.type === 'user') dto.userIds = a.userIds.filter(Boolean)
  else if (a.type === 'role' || a.type === 'position') {
    if (a.type === 'role') dto.roleCodes = a.codes.filter(Boolean)
    else {
      dto.positionCodes = a.codes.filter(Boolean)
      dto.scope = a.scope || 'company'
    }
  } else if (a.type === 'deptLeader' && a.deptId && a.deptId > 0) dto.deptId = a.deptId
  return dto
}

/** in 用逗号分隔多值；纯数字转 number，便于后端数值比较 */
function conditionValue(c: DraftCondition): string | number | (string | number)[] {
  const raw = c.value.trim()
  if (c.op === 'in') {
    return raw
      .split(/[,，\s]+/)
      .map((s) => s.trim())
      .filter(Boolean)
      .map(toScalar)
  }
  return toScalar(raw)
}

function toScalar(raw: string): string | number {
  if (raw !== '' && !Number.isNaN(Number(raw))) return Number(raw)
  return raw
}

const previewJson = computed(() => {
  try {
    return stringifyGraph(draftToGraph())
  } catch (err) {
    return errorText(err, '节点数据异常，无法生成 JSON')
  }
})

/* -------------------------------- 行编辑动作 -------------------------------- */
const codeOptions = computed<SelectOption[]>(() => nodes.value.map((n) => ({ label: `${n.name || nodeTypeLabel(n.type)}（${n.code}）`, value: n.code })))

function isFixed(node: DraftNode): boolean {
  const index = nodes.value.findIndex((n) => n.key === node.key)
  return (node.type === 'start' && index === 0) || (node.type === 'end' && index === nodes.value.length - 1)
}

function addNode(type: 'approval' | 'condition' | 'cc'): void {
  const code = shortCode()
  const node: DraftNode = {
    key: nextKey(),
    code,
    type,
    name: `${nodeTypeLabel(type)}节点`,
    mode: type === 'approval' ? 'orSign' : '',
    next: type === 'condition' ? '' : 'end',
    defaultNext: type === 'condition' ? 'end' : '',
    approvers: type === 'approval' ? [newApprover()] : [],
    branches: type === 'condition' ? [newBranch('end')] : [],
    ccUserIds: []
  }
  const end = nodes.value.findIndex((n) => n.type === 'end')
  const at = end >= 0 ? end : nodes.value.length
  nodes.value.splice(at, 0, node)
  // 线性链自动接线：把唯一指向 end 的前驱改指新节点
  const tail = nodes.value.filter((n) => n.key !== node.key && n.next === 'end')
  if (tail.length === 1) tail[0].next = code
}

function removeNode(node: DraftNode): void {
  const index = nodes.value.findIndex((n) => n.key === node.key)
  if (index < 0) return
  const target = node.next || node.defaultNext || node.branches[0]?.next || ''
  if (target) {
    for (const n of nodes.value) {
      if (n.key === node.key) continue
      if (n.next === node.code) n.next = target
      if (n.defaultNext === node.code) n.defaultNext = target
      for (const b of n.branches) if (b.next === node.code) b.next = target
    }
  }
  nodes.value.splice(index, 1)
}

function moveNode(node: DraftNode, offset: number): void {
  const from = nodes.value.findIndex((n) => n.key === node.key)
  const to = from + offset
  if (from < 0 || to < 0 || to >= nodes.value.length) return
  const target = nodes.value[to]
  if (isFixed(target)) return
  const [picked] = nodes.value.splice(from, 1)
  nodes.value.splice(to, 0, picked)
}

/** 改编码时同步引用，避免 next 悬空（进入编辑时记住原编码，失焦生效） */
const codeBefore = ref<Record<number, string>>({})

function rememberCode(node: DraftNode): void {
  codeBefore.value[node.key] = node.code
}

function applyCode(node: DraftNode): void {
  const old = codeBefore.value[node.key] ?? node.code
  const clean = node.code.trim()
  if (!clean) {
    node.code = old
    return
  }
  node.code = clean
  if (clean === old) return
  for (const n of nodes.value) {
    if (n.key === node.key) continue
    if (n.next === old) n.next = clean
    if (n.defaultNext === old) n.defaultNext = clean
    for (const b of n.branches) if (b.next === old) b.next = clean
  }
  codeBefore.value[node.key] = clean
}

function onTypeChange(node: DraftNode, type: string): void {
  node.type = type
  if (type === 'approval') {
    node.mode = node.mode || 'orSign'
    if (!node.approvers.length) node.approvers = [newApprover()]
    node.branches = []
  } else if (type === 'condition') {
    node.mode = ''
    node.approvers = []
    node.next = ''
    if (!node.branches.length) node.branches = [newBranch(node.defaultNext || 'end')]
  } else {
    node.mode = ''
    node.approvers = []
    node.branches = []
    if (!node.next) node.next = 'end'
  }
}

/* ---------------------------------- 打开/保存 --------------------------------- */
function openCreate(): void {
  designer.show = true
  designer.mode = 'create'
  designer.id = ''
  designer.flowCode = ''
  designer.baseVersion = 0
  designer.flowName = ''
  designer.category = ''
  designer.remark = ''
  designer.version = 0
  newVersionTip.value = false
  previewExpanded.value = []
  nodes.value = defaultDraft()
  void loadChoices()
  void loadCategories()
}

function openEdit(row: FlowDefDto, asNewVersion = false): void {
  designer.show = true
  designer.mode = 'edit'
  designer.id = row.id
  designer.flowCode = row.flowCode
  designer.baseVersion = row.flowVersion
  designer.flowName = row.flowName
  designer.category = row.category ?? ''
  designer.remark = row.remark ?? ''
  designer.version = row.version
  newVersionTip.value = asNewVersion
  previewExpanded.value = []
  nodes.value = graphToDraft(row.nodeJson)
  void loadChoices()
  void loadCategories()
}

async function save(): Promise<void> {
  if (!designer.flowName.trim()) {
    message.warning('请填写流程名称')
    return
  }
  const codes = nodes.value.map((n) => n.code.trim())
  if (codes.some((c) => !c)) {
    message.warning('存在未填编码的节点')
    return
  }
  if (new Set(codes).size !== codes.length) {
    message.warning('节点编码重复')
    return
  }
  const payload = {
    flowName: designer.flowName.trim(),
    category: designer.category || null,
    nodeJson: JSON.stringify(cleanGraph(draftToGraph())),
    remark: designer.remark || null,
    version: designer.version
  }
  saving.value = true
  try {
    if (designer.mode === 'create') {
      const created = await createFlowDef(payload)
      message.success(`已创建流程定义 ${created?.flowCode ?? ''} v${created?.flowVersion ?? 1}（停用状态，需启用后生效）`)
    } else {
      const saved = await updateFlowDef(designer.id, payload)
      message.success(`已保存为新版本 v${saved?.flowVersion ?? ''}（启用后新单据才走此版本）`)
    }
    designer.show = false
    await load()
    await loadCategories()
  } catch {
    /* 拦截器已提示（DSL 校验失败会回具体 msg） */
  } finally {
    saving.value = false
  }
}

function togglePreview(): void {
  previewExpanded.value = previewExpanded.value.length ? [] : ['json']
}

async function removeDef(row: FlowDefDto): Promise<void> {
  try {
    await deleteFlowDef(row.id)
    message.success('已删除')
    await load()
  } catch {
    /* 拦截器已提示 */
  }
}

const columns: DataTableColumns<FlowDefDto> = [
  { title: '流程编码', key: 'flowCode', width: 100 },
  { title: '流程名称', key: 'flowName', minWidth: 170, ellipsis: { tooltip: true } },
  {
    title: '分类',
    key: 'category',
    width: 120,
    render: (row) => row.category || h('span', { class: 'ps-muted' }, '—')
  },
  {
    title: '版本',
    key: 'flowVersion',
    width: 78,
    render: (row) => h(NTag, { size: 'small', bordered: false }, { default: () => `v${row.flowVersion}` })
  },
  {
    title: '状态',
    key: 'status',
    width: 96,
    render: (row) =>
      hasPerm('workflow:def:enable')
        ? h(NSwitch, {
            value: row.status === 1,
            checkedValue: true,
            uncheckedValue: false,
            size: 'small',
            onUpdateValue: (value: boolean) => void toggleStatus(row, value ? 1 : 0)
          })
        : h(NTag, { size: 'small', bordered: false, type: row.status === 1 ? 'success' : 'default' }, { default: () => (row.status === 1 ? '启用' : '停用') })
  },
  {
    title: '节点数',
    key: 'nodeJson',
    width: 84,
    render: (row) => {
      const graph = parseGraph(row.nodeJson)
      return `${graph.nodes.length}`
    }
  },
  { title: '创建时间', key: 'createTime', width: 168, render: (row) => formatDateTime(row.createTime) },
  {
    title: '操作',
    key: 'actions',
    width: 226,
    fixed: 'right',
    render: (row) => {
      const btns = [
        h(NButton, { size: 'tiny', type: 'primary', tertiary: true, onClick: () => openEdit(row) }, { default: () => '设计' })
      ]
      if (hasPerm('workflow:def:add')) {
        btns.push(
          h(NButton, { size: 'tiny', tertiary: true, onClick: () => openEdit(row, true) }, { default: () => '新版本' })
        )
      }
      if (hasPerm('workflow:def:delete')) {
        btns.push(
          h(
            NPopconfirm,
            { onPositiveClick: () => removeDef(row) },
            {
              trigger: () => h(NButton, { size: 'tiny', type: 'error', tertiary: true }, { default: () => '删除' }),
              default: () => `确认删除 ${row.flowName} v${row.flowVersion}？已有实例的定义不可删除`
            }
          )
        )
      }
      return h(NSpace, { size: 6, wrap: false }, { default: () => btns })
    }
  }
]

onMounted(() => {
  void loadCategories()
})
</script>

<template>
  <div class="ps-page">
    <NCard :bordered="false">
      <NSpace justify="space-between" align="end" :wrap="false" class="flow-def__bar">
        <NForm inline :model="queryParams" @submit.prevent="search">
          <NFormItem label="关键字" path="keyword">
            <NInput
              v-model:value="queryParams.keyword"
              placeholder="流程名称 / 编码"
              clearable
              style="width: 200px"
              @keyup.enter="search"
            />
          </NFormItem>
          <NFormItem label="分类" path="category">
            <NSelect
              v-model:value="queryParams.category"
              :options="categories"
              clearable
              filterable
              placeholder="全部分类"
              style="width: 160px"
              @update:value="search"
            />
          </NFormItem>
          <NFormItem label="状态" path="status">
            <NSelect
              :value="statusFilterValue"
              :options="STATUS_CHOICES"
              style="width: 120px"
              @update:value="pickStatus"
            />
          </NFormItem>
        </NForm>
        <NSpace>
          <NButton @click="reset">重置</NButton>
          <NButton :loading="loading" @click="search">查询</NButton>
          <NButton v-permission="'workflow:def:add'" type="primary" @click="openCreate">新建流程</NButton>
        </NSpace>
      </NSpace>

      <NDataTable
        remote
        size="small"
        :columns="columns"
        :data="data"
        :loading="loading"
        :pagination="pagination"
        :row-key="(row: FlowDefDto) => row.id"
        :scroll-x="1180"
      />
      <NAlert type="info" :bordered="false" class="flow-def__tip" title="版本与生效规则">
        编辑/新版本保存后同为「停用」状态：同编码只有一个启用版本，启用后新提交的单据才走该版本（在途实例仍按原定义走完）。
      </NAlert>
    </NCard>

    <NDrawer v-model:show="designer.show" :width="980" placement="right">
      <NDrawerContent closable :title="designer.mode === 'create' ? '新建流程定义' : `设计流程：${designer.flowName || designer.flowCode}`">
        <NScrollbar class="flow-def__scroll">
          <NForm label-placement="left" label-width="82" size="small">
            <NGrid responsive="screen" cols="2 s:2 m:4" :x-gap="12">
              <NFormItemGi label="流程名称" required>
                <NInput v-model:value="designer.flowName" maxlength="128" placeholder="如：费用报销流程" />
              </NFormItemGi>
              <NFormItemGi label="分类">
                <NSelect
                  v-model:value="designer.category"
                  :options="categories"
                  filterable
                  tag
                  clearable
                  placeholder="选择或输入新分类"
                />
              </NFormItemGi>
              <NFormItemGi label="流程编码">
                <NInput :value="designer.flowCode || '（保存后由后端生成）'" disabled />
              </NFormItemGi>
              <NFormItemGi label="当前版本">
                <NInput :value="designer.mode === 'create' ? 'v1' : `v${designer.baseVersion} → 保存成 v${designer.baseVersion + 1}`" disabled />
              </NFormItemGi>
            </NGrid>
            <NFormItem label="备注">
              <NInput v-model:value="designer.remark" maxlength="512" placeholder="选填" />
            </NFormItem>
          </NForm>

          <NAlert v-if="newVersionTip" type="warning" :bordered="false" class="flow-def__alert">
            本次基于 v{{ designer.baseVersion }} 复制节点，保存后生成 v{{ designer.baseVersion + 1 }} 新版本。
          </NAlert>

          <NDivider title-placement="left" class="flow-def__divider">节点顺序（{{ nodes.length }} 个节点）</NDivider>

          <div v-for="(node, index) in nodes" :key="node.key" class="flow-def__node">
            <div class="flow-def__node-head">
              <span class="flow-def__index">{{ index + 1 }}</span>
              <NSelect
                :value="node.type"
                :options="TYPE_CHOICES"
                :disabled="isFixed(node)"
                size="small"
                style="width: 116px"
                @update:value="(value: string) => onTypeChange(node, value)"
              />
              <NInput
                v-model:value="node.code"
                size="small"
                style="width: 128px"
                placeholder="节点编码"
                :disabled="node.type === 'start' || node.type === 'end'"
                @focus="rememberCode(node)"
                @blur="applyCode(node)"
              />
              <NInput v-model:value="node.name" size="small" style="width: 150px" placeholder="节点名称" />
              <NSelect
                v-if="node.type === 'approval'"
                v-model:value="node.mode"
                :options="[
                  { label: '或签', value: 'orSign' },
                  { label: '会签', value: 'countersign' },
                  { label: '依次审批', value: 'sequential' }
                ]"
                size="small"
                style="width: 108px"
              />
              <NSelect
                v-if="node.type !== 'end' && node.type !== 'condition'"
                v-model:value="node.next"
                :options="codeOptions.filter((o) => o.value !== node.code)"
                size="small"
                clearable
                placeholder="下一节点"
                style="width: 186px"
              />
              <NSelect
                v-if="node.type === 'condition'"
                v-model:value="node.defaultNext"
                :options="codeOptions.filter((o) => o.value !== node.code)"
                size="small"
                clearable
                placeholder="默认走向（可空=直接结束）"
                style="width: 220px"
              />
              <NSpace :size="2" class="flow-def__node-ops">
                <NButton size="tiny" quaternary :disabled="index === 0 || isFixed(nodes[index - 1])" @click="moveNode(node, -1)">
                  上移
                </NButton>
                <NButton
                  size="tiny"
                  quaternary
                  :disabled="index === nodes.length - 1 || isFixed(nodes[index + 1])"
                  @click="moveNode(node, 1)"
                >
                  下移
                </NButton>
                <NButton size="tiny" quaternary type="error" :disabled="isFixed(node)" @click="removeNode(node)">删除</NButton>
              </NSpace>
            </div>

            <!-- 审批人规则 -->
            <div v-if="node.type === 'approval'" class="flow-def__node-body">
              <div v-for="(approver, ai) in node.approvers" :key="ai" class="flow-def__approver">
                <span class="ps-muted">规则{{ ai + 1 }}</span>
                <NSelect
                  v-model:value="approver.type"
                  :options="APPROVER_CHOICES"
                  size="small"
                  style="width: 128px"
                />
                <NSelect
                  v-if="approver.type === 'user' || approver.type === 'submitterChoice'"
                  v-model:value="approver.userIds"
                  :options="userOpts"
                  multiple
                  filterable
                  size="small"
                  :disabled="approver.type === 'submitterChoice'"
                  :placeholder="approver.type === 'submitterChoice' ? '发起人提交时自选，无需配置' : '选择人员（可多选）'"
                  style="flex: 1 1 260px"
                />
                <NSelect
                  v-else-if="approver.type === 'role'"
                  v-model:value="approver.codes"
                  :options="roleOpts"
                  multiple
                  filterable
                  size="small"
                  placeholder="选择角色（可多选）"
                  style="flex: 1 1 260px"
                />
                <template v-else-if="approver.type === 'position'">
                  <NSelect
                    v-model:value="approver.codes"
                    :options="positionOpts"
                    multiple
                    filterable
                    size="small"
                    placeholder="选择岗位（可多选）"
                    style="flex: 1 1 220px"
                  />
                  <NSelect v-model:value="approver.scope" :options="SCOPE_CHOICES" size="small" style="width: 136px" />
                </template>
                <NInputNumber
                  v-else-if="approver.type === 'deptLeader'"
                  v-model:value="approver.deptId"
                  size="small"
                  :show-button="false"
                  placeholder="部门 id（留空=发起人部门逐级上找）"
                  style="flex: 1 1 220px"
                />
                <NButton
                  size="tiny"
                  quaternary
                  type="error"
                  :disabled="node.approvers.length <= 1"
                  @click="node.approvers.splice(ai, 1)"
                >
                  移除
                </NButton>
              </div>
              <NButton size="tiny" dashed @click="node.approvers.push(newApprover())">+ 添加审批人规则（多规则取并集）</NButton>
            </div>

            <!-- 抄送人员 -->
            <div v-else-if="node.type === 'cc'" class="flow-def__node-body">
              <NSelect
                v-model:value="node.ccUserIds"
                :options="userOpts"
                multiple
                filterable
                size="small"
                placeholder="抄送人员（可多选）"
                style="width: 100%"
              />
            </div>

            <!-- 条件分支 -->
            <div v-else-if="node.type === 'condition'" class="flow-def__node-body">
              <NCollapse :default-expanded-names="node.branches.map((_, i) => String(i))">
                <NCollapseItem
                  v-for="(branch, bi) in node.branches"
                  :key="bi"
                  :title="`分支 ${bi + 1}：${branch.name || '未命名'}（priority ${branch.priority}）`"
                  :name="String(bi)"
                >
                  <div class="flow-def__branch-line">
                    <NInput v-model:value="branch.name" size="small" placeholder="分支名称" style="width: 150px" />
                    <NInputNumber v-model:value="branch.priority" size="small" :show-button="false" placeholder="优先级" style="width: 96px" />
                    <NSelect
                      v-model:value="branch.next"
                      :options="codeOptions.filter((o) => o.value !== node.code)"
                      size="small"
                      placeholder="命中后走向"
                      style="width: 200px"
                    />
                    <NButton
                      size="tiny"
                      quaternary
                      type="error"
                      :disabled="node.branches.length <= 1"
                      @click="node.branches.splice(bi, 1)"
                    >
                      移除分支
                    </NButton>
                  </div>
                  <div v-for="(cond, ci) in branch.conditions" :key="ci" class="flow-def__cond">
                    <NInput v-model:value="cond.variable" size="small" placeholder="变量（如 amount）" style="width: 150px" />
                    <NSelect v-model:value="cond.op" :options="OP_CHOICES" size="small" style="width: 148px" />
                    <NInput
                      v-model:value="cond.value"
                      size="small"
                      :placeholder="cond.op === 'in' ? '多值用逗号分隔' : '比较值'"
                      style="flex: 1 1 160px"
                    />
                    <NButton
                      size="tiny"
                      quaternary
                      type="error"
                      :disabled="branch.conditions.length <= 1"
                      @click="branch.conditions.splice(ci, 1)"
                    >
                      删条件
                    </NButton>
                  </div>
                  <NButton size="tiny" dashed @click="branch.conditions.push({ variable: '', op: 'eq', value: '' })">
                    + 添加条件（分支内 AND 组合）
                  </NButton>
                </NCollapseItem>
              </NCollapse>
              <NButton size="tiny" dashed class="flow-def__add-branch" @click="node.branches.push(newBranch(node.defaultNext))">
                + 添加分支
              </NButton>
            </div>
          </div>

          <NSpace :size="8" class="flow-def__add">
            <NButton size="small" dashed @click="addNode('approval')">+ 审批节点</NButton>
            <NButton size="small" dashed @click="addNode('condition')">+ 条件分支</NButton>
            <NButton size="small" dashed @click="addNode('cc')">+ 抄送节点</NButton>
          </NSpace>

          <NCollapse v-model:expanded-names="previewExpanded" class="flow-def__preview">
            <NCollapseItem title="保存前预览 nodeJson" name="json">
              <NCode :code="previewJson" language="json" show-line-numbers word-wrap />
            </NCollapseItem>
          </NCollapse>
        </NScrollbar>

        <template #footer>
          <NSpace justify="end">
            <NButton @click="designer.show = false">取消</NButton>
            <NButton :pressed="previewExpanded.length > 0" @click="togglePreview">
              {{ previewExpanded.length ? '收起预览' : '预览 JSON' }}
            </NButton>
            <NButton
              v-permission="designer.mode === 'create' ? 'workflow:def:add' : 'workflow:def:edit'"
              type="primary"
              :loading="saving"
              @click="save"
            >
              保存{{ designer.mode === 'create' ? '（新流程）' : '（新版本）' }}
            </NButton>
          </NSpace>
        </template>
      </NDrawerContent>
    </NDrawer>
  </div>
</template>

<style scoped>
.flow-def__bar {
  margin-bottom: 12px;
}

.flow-def__tip {
  margin-top: 14px;
}

.flow-def__scroll {
  height: calc(100vh - 190px);
  padding-right: 8px;
}

.flow-def__alert,
.flow-def__divider {
  margin-top: 4px;
}

.flow-def__node {
  border: 1px solid rgba(100, 116, 139, 0.24);
  border-radius: 8px;
  padding: 8px 10px;
  margin-bottom: 10px;
}

.flow-def__node-head {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.flow-def__node-body {
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px dashed rgba(100, 116, 139, 0.2);
}

.flow-def__index {
  display: inline-grid;
  place-items: center;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: var(--ps-primary-soft);
  color: var(--ps-primary);
  font-size: 12px;
  font-weight: 600;
  flex: none;
}

.flow-def__node-ops {
  margin-left: auto;
}

.flow-def__approver,
.flow-def__branch-line {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 8px;
}

.flow-def__cond {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 6px;
}

.flow-def__add,
.flow-def__add-branch,
.flow-def__preview {
  margin-top: 10px;
}
</style>
