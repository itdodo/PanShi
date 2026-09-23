<script setup lang="ts">
import type { FlowConditionDto } from './flowGraph'
import type { FlowSegment } from './flowTree'
import { nodeModeLabel, nodeTypeLabel } from './flowEnums'

/** 只读流程图轨道（递归）：单节点卡片 / 条件块（并列分支列，列内再递归）。 */
defineOptions({ name: 'FlowTrack' })
defineProps<{ track: FlowSegment[]; highlight?: string }>()

const OP: Record<string, string> = { lt: '<', le: '≤', gt: '>', ge: '≥', eq: '=', ne: '≠', in: '∈', contains: '包含' }

function condText(c: FlowConditionDto): string {
  const val = Array.isArray(c.value) ? c.value.join(' / ') : String(c.value ?? '')
  return `${c.variable} ${OP[c.op] ?? c.op} ${val}`
}
</script>

<template>
  <div class="ft">
    <template v-for="(seg, i) in track" :key="seg.kind === 'node' ? seg.node.code : 'cond' + i">
      <div class="ft__line" />

      <!-- 单节点 -->
      <div
        v-if="seg.kind === 'node'"
        class="ft__card"
        :class="['ft__card--' + seg.node.type, { 'ft__card--hl': highlight && highlight === seg.node.code }]"
      >
        <div class="ft__card-top">
          <span class="ft__badge">{{ nodeTypeLabel(seg.node.type) }}</span>
          <span class="ft__name">{{ seg.node.name || seg.node.code }}</span>
        </div>
        <div v-if="seg.node.type === 'approval'" class="ft__meta">
          {{ nodeModeLabel(seg.node.mode) }} · {{ (seg.node.approvers ?? []).length || 0 }} 条审批规则
        </div>
        <div v-else-if="seg.node.type === 'cc'" class="ft__meta">
          抄送 {{ (seg.node.ccUserIds ?? []).length || 0 }} 人
        </div>
      </div>

      <!-- 条件块 -->
      <div v-else class="ft__cond">
        <div class="ft__cond-head">
          <span class="ft__badge ft__badge--condition">条件分支</span>
          <span class="ft__name">{{ seg.node.name || seg.node.code }}</span>
        </div>
        <div class="ft__cols">
          <div v-for="(b, bi) in seg.branches" :key="bi" class="ft__col">
            <div class="ft__col-head">
              <span class="ft__col-name">{{ b.name || '分支' + (bi + 1) }}</span>
              <span v-for="(c, ci) in b.conditions" :key="'c' + ci" class="ft__chip">{{ condText(c) }}</span>
              <span v-if="b.isDefault" class="ft__chip ft__chip--default">兜底</span>
            </div>
            <FlowTrack :track="b.track" :highlight="highlight" />
            <div v-if="!b.track.length" class="ft__empty">（直通）</div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.ft {
  display: flex;
  flex-direction: column;
  align-items: center;
  width: 100%;
}

.ft__line {
  width: 2px;
  height: 22px;
  background: var(--ps-card-border);
}

.ft__card {
  width: 100%;
  max-width: 320px;
  border: 1px solid var(--ps-card-border);
  border-left: 3px solid var(--ps-primary);
  border-radius: 10px;
  padding: 10px 12px;
  background: var(--n-color, #fff);
  box-shadow: 0 1px 2px rgba(15, 23, 42, 0.05);
}

.ft__card--start { border-left-color: #22c55e; }
.ft__card--end { border-left-color: #94a3b8; }
.ft__card--cc { border-left-color: #06b6d4; }
.ft__card--condition { border-left-color: #f59e0b; }
.ft__card--hl {
  border-color: var(--ps-primary);
  box-shadow: 0 0 0 2px var(--ps-primary-soft);
}

.ft__card-top {
  display: flex;
  align-items: center;
  gap: 8px;
}

.ft__badge {
  flex: none;
  font-size: 11px;
  line-height: 18px;
  padding: 0 7px;
  border-radius: 9px;
  background: var(--ps-primary-soft);
  color: var(--ps-primary);
}

.ft__badge--condition { background: rgba(245, 158, 11, 0.14); color: #d97706; }
.ft__card--start .ft__badge { background: rgba(34, 197, 94, 0.14); color: #16a34a; }
.ft__card--end .ft__badge { background: rgba(148, 163, 184, 0.18); color: #64748b; }
.ft__card--cc .ft__badge { background: rgba(6, 182, 212, 0.14); color: #0891b2; }

.ft__name {
  font-size: 14px;
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ft__meta {
  margin-top: 6px;
  font-size: 12px;
  color: var(--ps-text-3);
}

.ft__cond {
  width: 100%;
  border: 1px dashed var(--ps-card-border);
  border-radius: 12px;
  padding: 10px 12px 14px;
}

.ft__cond-head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;
}

.ft__cols {
  display: flex;
  gap: 12px;
  overflow-x: auto;
  align-items: flex-start;
}

.ft__col {
  flex: 1 1 0;
  min-width: 180px;
  display: flex;
  flex-direction: column;
  align-items: center;
}

.ft__col-head {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  align-items: center;
  justify-content: center;
  margin-bottom: 4px;
  width: 100%;
}

.ft__col-name {
  font-size: 12px;
  font-weight: 600;
  color: var(--ps-text-1);
}

.ft__chip {
  font-size: 11px;
  padding: 1px 7px;
  border-radius: 8px;
  background: rgba(148, 163, 184, 0.16);
  color: var(--ps-text-3);
}

.ft__chip--default { background: rgba(245, 158, 11, 0.14); color: #d97706; }

.ft__empty {
  font-size: 12px;
  color: var(--ps-text-3);
  padding: 4px 0;
}
</style>
