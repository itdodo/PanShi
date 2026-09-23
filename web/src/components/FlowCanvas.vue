<script setup lang="ts">
import { computed } from 'vue'
import type { FlowGraphDto } from './flowGraph'
import { buildFlowTree } from './flowTree'
import FlowTrack from './FlowTrack.vue'

/** 流程图：把 FlowGraph DSL 渲染成钉钉式纵向树。highlight=高亮节点；editable=可点选/插入。 */
const props = defineProps<{ graph: FlowGraphDto; highlight?: string; editable?: boolean; selected?: string | null }>()
defineEmits<{ select: [code: string]; insert: [afterCode: string, type: string] }>()
const tree = computed(() => buildFlowTree(props.graph))
</script>

<template>
  <div class="fc">
    <FlowTrack
      :track="tree.track"
      :highlight="props.highlight"
      :editable="props.editable"
      :selected="props.selected"
      @select="(code: string) => $emit('select', code)"
      @insert="(code: string, type: string) => $emit('insert', code, type)"
    />
  </div>
</template>

<style scoped>
.fc {
  display: flex;
  justify-content: center;
  padding: 8px 4px 16px;
  overflow-x: auto;
}
</style>
