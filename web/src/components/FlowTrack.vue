<script setup lang="ts">
import { NDropdown, type DropdownOption } from 'naive-ui'
import type { FlowConditionDto } from './flowGraph'
import type { FlowSegment } from './flowTree'
import { nodeModeLabel, nodeTypeLabel } from './flowEnums'

/** 流程图轨道（递归）：单节点卡片 / 条件块（并列分支列，列内再递归）。editable 时卡片可点选、卡片间可插入。 */
defineOptions({ name: 'FlowTrack' })
const props = withDefaults(
  defineProps<{ track: FlowSegment[]; highlight?: string; editable?: boolean; selected?: string | null }>(),
  { editable: false, selected: null }
)
const emit = defineEmits<{ select: [code: string]; insert: [afterCode: string, type: string]; remove: [code: string] }>()

const OP: Record<string, string> = { lt: '<', le: '≤', gt: '>', ge: '≥', eq: '=', ne: '≠', in: '∈', contains: '包含' }
const INSERT_OPTIONS: DropdownOption[] = [
  { label: '审批节点', key: 'approval' },
  { label: '抄送节点', key: 'cc' },
  { label: '条件分支', key: 'condition' }
]

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
      <template v-if="seg.kind === 'node'">
        <div
          class="ft__card"
          :class="[
            'ft__card--' + seg.node.type,
            { 'ft__card--hl': highlight && highlight === seg.node.code },
            { 'ft__card--sel': selected === seg.node.code },
            { 'ft__card--click': editable }
          ]"
          @click="editable && emit('select', seg.node.code)"
        >
          <div class="ft__card-top">
            <span class="ft__badge">{{ nodeTypeLabel(seg.node.type) }}</span>
            <span class="ft__name">{{ seg.node.name || seg.node.code }}</span>
          </div>
          <div v-if="seg.node.type === 'approval'" class="ft__meta">
            {{ nodeModeLabel(seg.node.mode) }} · {{ (seg.node.approvers ?? []).length || 0 }} 条审批规则
          </div>
          <div v-else-if="seg.node.type === 'cc'" class="ft__meta">抄送 {{ (seg.node.ccUserIds ?? []).length || 0 }} 人</div>
          <div v-if="editable && seg.node.type !== 'start' && seg.node.type !== 'end'" class="ft__card-hint">点击配置</div>
        </div>

        <!-- 卡片后插入「+」（start/普通节点后都可插；end 不插） -->
        <template v-if="editable && seg.node.type !== 'end'">
          <div class="ft__line ft__line--short" />
          <NDropdown trigger="click" :options="INSERT_OPTIONS" @select="(key: string) => emit('insert', seg.node.code, key)">
            <button type="button" class="ft__add" aria-label="在此后插入节点">＋</button>
          </NDropdown>
        </template>
      </template>

      <!-- 条件块 -->
      <div v-else class="ft__cond">
        <div class="ft__cond-head">
          <span class="ft__badge ft__badge--condition">条件分支</span>
          <span class="ft__name ft__name--click" :class="{ 'ft__card--click': editable }" @click="editable && emit('select', seg.node.code)">
            {{ seg.node.name || seg.node.code }}
          </span>
        </div>
        <div class="ft__cols">
          <div v-for="(b, bi) in seg.branches" :key="bi" class="ft__col">
            <div class="ft__col-head">
              <span class="ft__col-name">{{ b.name || '分支' + (bi + 1) }}</span>
              <span v-for="(c, ci) in b.conditions" :key="'c' + ci" class="ft__chip">{{ condText(c) }}</span>
              <span v-if="b.isDefault" class="ft__chip ft__chip--default">兜底</span>
            </div>
            <FlowTrack
              :track="b.track"
              :highlight="highlight"
              :editable="editable"
              :selected="selected"
              @select="(code: string) => emit('select', code)"
              @insert="(code: string, type: string) => emit('insert', code, type)"
            />
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

.ft__line--short {
  height: 12px;
}

.ft__add {
  width: 24px;
  height: 24px;
  margin: 2px 0;
  border: 1px solid var(--ps-primary);
  border-radius: 50%;
  background: var(--ps-primary-soft);
  color: var(--ps-primary);
  font-size: 15px;
  line-height: 1;
  cursor: pointer;
}

.ft__add:hover { background: var(--ps-primary); color: #fff; }

.ft__card {
  position: relative;
  width: 100%;
  max-width: 320px;
  border: 1px solid var(--ps-card-border);
  border-left: 3px solid var(--ps-primary);
  border-radius: 10px;
  padding: 10px 12px;
  background: var(--n-color, #fff);
  box-shadow: 0 1px 2px rgba(15, 23, 42, 0.05);
}

.ft__card--click { cursor: pointer; }
.ft__card--click:hover { border-color: var(--ps-primary); }
.ft__card--sel { border-color: var(--ps-primary); box-shadow: 0 0 0 2px var(--ps-primary-soft); }
.ft__card--start { border-left-color: #22c55e; }
.ft__card--end { border-left-color: #94a3b8; }
.ft__card--cc { border-left-color: #06b6d4; }
.ft__card--condition { border-left-color: #f59e0b; }
.ft__card--hl { border-color: var(--ps-primary); box-shadow: 0 0 0 2px var(--ps-primary-soft); }
.ft__card-hint { position: absolute; top: 8px; right: 10px; font-size: 11px; color: var(--ps-text-3); }

.ft__card-top { display: flex; align-items: center; gap: 8px; }

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

.ft__name { font-size: 14px; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ft__name--click { cursor: pointer; }
.ft__meta { margin-top: 6px; font-size: 12px; color: var(--ps-text-3); }

.ft__cond { width: 100%; border: 1px dashed var(--ps-card-border); border-radius: 12px; padding: 10px 12px 14px; }
.ft__cond-head { display: flex; align-items: center; gap: 8px; margin-bottom: 10px; }
.ft__cols { display: flex; gap: 12px; overflow-x: auto; align-items: flex-start; }
.ft__col { flex: 1 1 0; min-width: 180px; display: flex; flex-direction: column; align-items: center; }
.ft__col-head { display: flex; flex-wrap: wrap; gap: 4px; align-items: center; justify-content: center; margin-bottom: 4px; width: 100%; }
.ft__col-name { font-size: 12px; font-weight: 600; color: var(--ps-text-1); }
.ft__chip { font-size: 11px; padding: 1px 7px; border-radius: 8px; background: rgba(148, 163, 184, 0.16); color: var(--ps-text-3); }
.ft__chip--default { background: rgba(245, 158, 11, 0.14); color: #d97706; }
.ft__empty { font-size: 12px; color: var(--ps-text-3); padding: 4px 0; }
</style>
