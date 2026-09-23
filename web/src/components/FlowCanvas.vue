<script setup lang="ts">
import { computed } from 'vue'
import type { FlowGraphDto } from './flowGraph'
import { buildFlowTree } from './flowTree'
import FlowTrack from './FlowTrack.vue'

/** 只读流程图：把 FlowGraph DSL 渲染成钉钉式纵向树。highlight=当前节点 code（实例/待办页高亮用）。 */
const props = defineProps<{ graph: FlowGraphDto; highlight?: string }>()
const tree = computed(() => buildFlowTree(props.graph))
</script>

<template>
  <div class="fc">
    <FlowTrack :track="tree.track" :highlight="props.highlight" />
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
