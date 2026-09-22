<script setup lang="ts">
import { reactive, ref } from 'vue'
import {
  NAlert,
  NButton,
  NForm,
  NFormItem,
  NInput,
  NModal,
  NPopconfirm,
  NRadio,
  NRadioGroup,
  NSpace,
  NSpin,
  type SelectOption
} from 'naive-ui'
import { addsignTask, actTask, instanceDetail, returnTask, transferTask } from '@/api/flow'
import type { FlowInstanceDetail, FlowTaskDto } from '@/api/flow'
import { userOptions, type Option } from '@/api/admin'
import { errorText } from '@/api/types'
import { message } from '@/utils/feedback'
import { orderedNodes, parseGraph, returnTargets } from './flowGraph'

/**
 * 待办行内审批动作组：同意 / 拒绝（NPopconfirm + 意见）· 转办 · 加签（前/后）· 驳回至节点。
 * 全部走真实契约：/sys/flow/task/{id}/act|transfer|addsign|return，成功后 emit('done')。
 */
const props = withDefaults(
  defineProps<{
    task: FlowTaskDto
    /** 按钮尺寸（表格内默认 tiny） */
    size?: 'tiny' | 'small'
  }>(),
  { size: 'tiny' }
)

const emit = defineEmits<{ (e: 'done'): void }>()

/* ------------------------------ 选项/详情缓存 ------------------------------ */
let userCache: Option[] | null = null
const userOptionsRef = ref<SelectOption[]>([])
const detailCache = new Map<string, FlowInstanceDetail>()

async function ensureUsers(): Promise<void> {
  if (userCache) return
  try {
    userCache = await userOptions()
    userOptionsRef.value = (userCache ?? []).map((o) => ({ label: o.label, value: o.value }))
  } catch {
    userCache = null
  }
}

async function loadDetail(instanceId: string): Promise<FlowInstanceDetail | null> {
  const hit = detailCache.get(instanceId)
  if (hit) return hit
  const detail = await instanceDetail(instanceId)
  if (detail) detailCache.set(instanceId, detail)
  return detail
}

/* --------------------------------- 同意/拒绝 -------------------------------- */
const busy = ref('')
const comments = reactive({ approve: '', reject: '' })

async function doAct(action: 'approve' | 'reject'): Promise<boolean> {
  busy.value = action
  try {
    await actTask(props.task.id, action, comments[action].trim() || undefined)
    message.success(action === 'approve' ? '已同意' : '已拒绝')
    comments[action] = ''
    emit('done')
    return true
  } catch {
    return false
  } finally {
    busy.value = ''
  }
}

/* ---------------------------------- 转办 ---------------------------------- */
const showTransfer = ref(false)
const transferForm = reactive({ toUserId: null as string | null, comment: '' })

async function openTransfer(): Promise<void> {
  transferForm.toUserId = null
  transferForm.comment = ''
  showTransfer.value = true
  await ensureUsers()
}

async function submitTransfer(): Promise<void> {
  if (!transferForm.toUserId) {
    message.warning('请选择转办对象')
    return
  }
  busy.value = 'transfer'
  try {
    await transferTask(props.task.id, transferForm.toUserId, transferForm.comment.trim() || undefined)
    message.success('已转办')
    showTransfer.value = false
    emit('done')
  } catch {
    /* 拦截器已提示 */
  } finally {
    busy.value = ''
  }
}

/* ---------------------------------- 加签 ---------------------------------- */
const showAddsign = ref(false)
const addsignForm = reactive({ userIds: [] as string[], after: false, comment: '' })

async function openAddsign(): Promise<void> {
  addsignForm.userIds = []
  addsignForm.after = false
  addsignForm.comment = ''
  showAddsign.value = true
  await ensureUsers()
}

async function submitAddsign(): Promise<void> {
  if (!addsignForm.userIds.length) {
    message.warning('请选择加签人员')
    return
  }
  busy.value = 'addsign'
  try {
    await addsignTask(props.task.id, addsignForm.userIds, addsignForm.after, addsignForm.comment.trim() || undefined)
    message.success('已加签')
    showAddsign.value = false
    emit('done')
  } catch {
    /* 拦截器已提示 */
  } finally {
    busy.value = ''
  }
}

/* -------------------------------- 驳回至节点 -------------------------------- */
const showReturn = ref(false)
const returnLoading = ref(false)
const returnError = ref('')
const returnOptions = ref<SelectOption[]>([])
const returnForm = reactive({ targetNodeCode: null as string | null, comment: '' })

async function openReturn(): Promise<void> {
  returnForm.targetNodeCode = null
  returnForm.comment = ''
  returnOptions.value = []
  returnError.value = ''
  showReturn.value = true
  returnLoading.value = true
  try {
    const detail = await loadDetail(props.task.instanceId)
    const graph = parseGraph(detail?.nodeJson)
    const ordered = orderedNodes(graph)
    const index = ordered.findIndex((n) => n.code === props.task.nodeCode)
    if (index < 0 || ordered[index].type !== 'approval') {
      returnError.value = `当前节点「${props.task.nodeName}」非审批节点，不支持驳回至节点`
      return
    }
    const candidates = returnTargets(ordered.slice(0, index), graph.entry)
    if (!candidates.length) {
      returnError.value = '本审批节点之前没有可驳回的节点，请改用「拒绝」'
      return
    }
    returnOptions.value = candidates.map((n) => ({ label: `${n.name || n.code}（${n.code}）`, value: n.code }))
    returnForm.targetNodeCode = candidates[candidates.length - 1].code
  } catch (err) {
    returnError.value = errorText(err, '流程节点加载失败')
  } finally {
    returnLoading.value = false
  }
}

async function submitReturn(): Promise<void> {
  if (!returnForm.targetNodeCode) {
    message.warning('请选择驳回目标节点')
    return
  }
  busy.value = 'return'
  try {
    await returnTask(props.task.id, returnForm.targetNodeCode, returnForm.comment.trim() || undefined)
    message.success('已驳回至该节点')
    showReturn.value = false
    emit('done')
  } catch {
    /* 拦截器已提示 */
  } finally {
    busy.value = ''
  }
}
</script>

<template>
  <div class="approval-actions" @click.stop>
    <NSpace :size="6" :wrap="false">
      <NPopconfirm :show-icon="false" @positive-click="doAct('approve')">
        <template #trigger>
          <NButton :size="size" type="primary" :loading="busy === 'approve'" :disabled="!!busy && busy !== 'approve'">
            同意
          </NButton>
        </template>
        <div class="approval-actions__pop">
          <div class="ps-muted approval-actions__pop-title">确认同意「{{ task.nodeName }}」</div>
          <NInput v-model:value="comments.approve" type="textarea" :rows="2" maxlength="500" placeholder="审批意见（选填）" />
        </div>
      </NPopconfirm>

      <NPopconfirm :show-icon="false" @positive-click="doAct('reject')">
        <template #trigger>
          <NButton :size="size" type="error" ghost :loading="busy === 'reject'" :disabled="!!busy && busy !== 'reject'">
            拒绝
          </NButton>
        </template>
        <div class="approval-actions__pop">
          <div class="ps-muted approval-actions__pop-title">确认拒绝「{{ task.nodeName }}」？实例将直接结束</div>
          <NInput v-model:value="comments.reject" type="textarea" :rows="2" maxlength="500" placeholder="拒绝理由（选填）" />
        </div>
      </NPopconfirm>

      <NButton :size="size" tertiary type="info" :disabled="!!busy" @click="openTransfer">转办</NButton>
      <NButton :size="size" tertiary :disabled="!!busy" @click="openAddsign">加签</NButton>
      <NButton :size="size" tertiary type="warning" :disabled="!!busy" @click="openReturn">驳回</NButton>
    </NSpace>

    <!-- 转办 -->
    <NModal
      v-model:show="showTransfer"
      preset="card"
      title="转办给他人"
      :style="{ width: '460px' }"
      :mask-closable="false"
    >
      <NForm label-placement="left" label-width="84">
        <NFormItem label="当前节点">
          <span>{{ task.nodeName }}（{{ task.nodeCode }}）</span>
        </NFormItem>
        <NFormItem label="转办给" required>
          <NSelect
            v-model:value="transferForm.toUserId"
            :options="userOptionsRef"
            filterable
            clearable
            placeholder="搜索并选择人员"
          />
        </NFormItem>
        <NFormItem label="说明">
          <NInput v-model:value="transferForm.comment" type="textarea" :rows="2" maxlength="500" placeholder="选填" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton :disabled="busy === 'transfer'" @click="showTransfer = false">取消</NButton>
          <NButton type="primary" :loading="busy === 'transfer'" @click="submitTransfer">确认转办</NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 加签 -->
    <NModal
      v-model:show="showAddsign"
      preset="card"
      title="加签"
      :style="{ width: '480px' }"
      :mask-closable="false"
    >
      <NForm label-placement="left" label-width="84">
        <NFormItem label="加签方式">
          <NRadioGroup v-model:value="addsignForm.after">
            <NRadio :value="false">前加签（并入本节点共同把关）</NRadio>
            <NRadio :value="true">后加签（本节点通过后处理）</NRadio>
          </NRadioGroup>
        </NFormItem>
        <NFormItem label="加签人员" required>
          <NSelect
            v-model:value="addsignForm.userIds"
            :options="userOptionsRef"
            multiple
            filterable
            clearable
            placeholder="可多选"
          />
        </NFormItem>
        <NFormItem label="说明">
          <NInput v-model:value="addsignForm.comment" type="textarea" :rows="2" maxlength="500" placeholder="选填" />
        </NFormItem>
      </NForm>
      <template #footer>
        <NSpace justify="end">
          <NButton :disabled="busy === 'addsign'" @click="showAddsign = false">取消</NButton>
          <NButton type="primary" :loading="busy === 'addsign'" @click="submitAddsign">确认加签</NButton>
        </NSpace>
      </template>
    </NModal>

    <!-- 驳回至节点 -->
    <NModal
      v-model:show="showReturn"
      preset="card"
      title="驳回至指定节点"
      :style="{ width: '480px' }"
      :mask-closable="false"
    >
      <NSpin :show="returnLoading">
        <NAlert v-if="returnError" type="warning" :bordered="false" style="margin-bottom: 12px">
          {{ returnError }}
        </NAlert>
        <NForm v-else label-placement="left" label-width="84">
          <NFormItem label="当前节点">
            <span>{{ task.nodeName }}（{{ task.nodeCode }}）</span>
          </NFormItem>
          <NFormItem label="驳回至" required>
            <NSelect
              v-model:value="returnForm.targetNodeCode"
              :options="returnOptions"
              placeholder="选择 start 或本节点之前的审批节点"
            />
          </NFormItem>
          <NFormItem label="驳回理由">
            <NInput v-model:value="returnForm.comment" type="textarea" :rows="2" maxlength="500" placeholder="选填" />
          </NFormItem>
        </NForm>
      </NSpin>
      <template #footer>
        <NSpace justify="end">
          <NButton @click="showReturn = false">取消</NButton>
          <NButton
            type="warning"
            :disabled="!returnOptions.length || returnLoading"
            :loading="busy === 'return'"
            @click="submitReturn"
          >
            确认驳回
          </NButton>
        </NSpace>
      </template>
    </NModal>
  </div>
</template>

<style scoped>
.approval-actions__pop {
  width: 260px;
}

.approval-actions__pop-title {
  margin-bottom: 6px;
  font-size: 13px;
}
</style>
