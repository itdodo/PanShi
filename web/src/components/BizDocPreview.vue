<script setup lang="ts">
import { ref, watch } from 'vue'
import { NDescriptions, NDescriptionsItem, NEmpty, NSpace, NTag } from 'naive-ui'
import { getExpense, getPurchase, type ExpenseDto, type PurchaseDto } from '@/api/biz'
import { API_BASE } from '@/api/http'
import { formatDateTime } from '@/utils/format'
import { docStatusMeta } from './flowEnums'
import { categoryLabel, loadExpenseCategories } from './bizDict'
import type { CategoryOption } from './bizDict'

/**
 * 业务单据只读预览（报销单 / 采购申请单）：NDescriptions 描述列表。
 * props.table 决定取哪个详情接口，id 变更即重新加载。
 */
const props = defineProps<{
  table: 'biz_expense' | 'biz_purchase_request'
  id: string
}>()

const loading = ref(false)
const expense = ref<ExpenseDto | null>(null)
const purchase = ref<PurchaseDto | null>(null)
const categories = ref<CategoryOption[]>([])

async function load(): Promise<void> {
  expense.value = null
  purchase.value = null
  if (!props.id) return
  loading.value = true
  try {
    if (props.table === 'biz_expense') expense.value = await getExpense(props.id)
    else purchase.value = await getPurchase(props.id)
    if (props.table === 'biz_expense' && !categories.value.length) categories.value = await loadExpenseCategories()
  } catch {
    /* 拦截器已提示 */
  } finally {
    loading.value = false
  }
}

watch(() => [props.table, props.id] as const, load, { immediate: true })

const doc = () => expense.value ?? purchase.value

function attachmentUrl(id: string): string {
  return `${API_BASE}/file/${encodeURIComponent(id)}/download`
}
</script>

<template>
  <div class="biz-doc-preview">
    <NEmpty v-if="!loading && !doc()" description="单据不存在或无权查看" size="small" />
    <template v-else>
      <!-- 报销单 -->
      <NDescriptions
        v-if="expense"
        :column="2"
        bordered
        size="small"
        label-placement="left"
        title="报销单详情"
      >
        <NDescriptionsItem label="报销单号">{{ expense.docNo }}</NDescriptionsItem>
        <NDescriptionsItem label="状态">
          <NTag size="small" :type="docStatusMeta(expense.status).type" :bordered="false">
            {{ docStatusMeta(expense.status).label }}
          </NTag>
        </NDescriptionsItem>
        <NDescriptionsItem label="申请人">{{ expense.ownerUserName }}</NDescriptionsItem>
        <NDescriptionsItem label="报销金额">
          <strong>¥{{ Number(expense.amount ?? 0).toFixed(2) }}</strong>
        </NDescriptionsItem>
        <NDescriptionsItem label="报销类别">
          {{ categoryLabel(expense.category, categories) }}
        </NDescriptionsItem>
        <NDescriptionsItem label="申请时间">{{ formatDateTime(expense.createTime) }}</NDescriptionsItem>
        <NDescriptionsItem label="事由" :span="2">{{ expense.reason || '-' }}</NDescriptionsItem>
        <NDescriptionsItem label="附件" :span="2">
          <NSpace v-if="expense.attachmentIds?.length" :size="8" wrap>
            <a v-for="fid in expense.attachmentIds" :key="fid" :href="attachmentUrl(fid)" target="_blank" rel="noopener">
              附件 {{ fid }}
            </a>
          </NSpace>
          <span v-else class="ps-muted">无</span>
        </NDescriptionsItem>
        <NDescriptionsItem v-if="expense.instanceId" label="审批实例" :span="2">
          {{ expense.instanceId }}
        </NDescriptionsItem>
      </NDescriptions>

      <!-- 采购申请单 -->
      <NDescriptions
        v-else-if="purchase"
        :column="2"
        bordered
        size="small"
        label-placement="left"
        title="采购申请单详情"
      >
        <NDescriptionsItem label="申请单号">{{ purchase.docNo }}</NDescriptionsItem>
        <NDescriptionsItem label="状态">
          <NTag size="small" :type="docStatusMeta(purchase.status).type" :bordered="false">
            {{ docStatusMeta(purchase.status).label }}
          </NTag>
        </NDescriptionsItem>
        <NDescriptionsItem label="申请人">{{ purchase.ownerUserName }}</NDescriptionsItem>
        <NDescriptionsItem label="品名">{{ purchase.itemName }}</NDescriptionsItem>
        <NDescriptionsItem label="数量">{{ purchase.quantity }}</NDescriptionsItem>
        <NDescriptionsItem label="预算金额">
          <strong>¥{{ Number(purchase.amount ?? 0).toFixed(2) }}</strong>
        </NDescriptionsItem>
        <NDescriptionsItem label="申请时间">{{ formatDateTime(purchase.createTime) }}</NDescriptionsItem>
        <NDescriptionsItem label="事由" :span="2">{{ purchase.reason || '-' }}</NDescriptionsItem>
        <NDescriptionsItem label="附件" :span="2">
          <NSpace v-if="purchase.attachmentIds?.length" :size="8" wrap>
            <a v-for="fid in purchase.attachmentIds" :key="fid" :href="attachmentUrl(fid)" target="_blank" rel="noopener">
              附件 {{ fid }}
            </a>
          </NSpace>
          <span v-else class="ps-muted">无</span>
        </NDescriptionsItem>
        <NDescriptionsItem v-if="purchase.instanceId" label="审批实例" :span="2">
          {{ purchase.instanceId }}
        </NDescriptionsItem>
      </NDescriptions>
    </template>
  </div>
</template>

<style scoped>
.biz-doc-preview {
  min-height: 120px;
}
</style>
