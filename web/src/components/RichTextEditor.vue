<script setup lang="ts">
import { onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import { Editor, Toolbar } from '@wangeditor/editor-for-vue'
import type { IDomEditor, IEditorConfig, IToolbarConfig } from '@wangeditor/editor'
import '@wangeditor/editor/dist/css/style.css'

/**
 * 富文本编辑器（wangEditor）封装。
 * ⚠️ 红线 #10：编辑器必须在「可见容器」内初始化——父级须用 v-if 在弹层 after-enter 之后再挂载本组件，
 * 切勿在隐藏(display:none)状态下初始化（会零尺寸/工具栏错位）。组件卸载时销毁编辑器实例，避免内存泄漏。
 */
const props = withDefaults(
  defineProps<{ modelValue?: string; placeholder?: string; minHeight?: number }>(),
  { modelValue: '', placeholder: '请输入正文…', minHeight: 260 }
)
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

const editorRef = shallowRef<IDomEditor>()
const html = ref(props.modelValue ?? '')

watch(
  () => props.modelValue,
  (v) => {
    const next = v ?? ''
    if (next !== html.value) html.value = next
  }
)
watch(html, (v) => emit('update:modelValue', v))

// 去掉需要后端上传接口的菜单（图片走「网络图片 URL」），保持底座零外部依赖
const toolbarConfig: Partial<IToolbarConfig> = { excludeKeys: ['group-video', 'uploadImage', 'fullScreen'] }
const editorConfig: Partial<IEditorConfig> = { placeholder: props.placeholder }

function handleCreated(editor: IDomEditor): void {
  editorRef.value = editor
}

onBeforeUnmount(() => {
  editorRef.value?.destroy()
  editorRef.value = undefined
})
</script>

<template>
  <div class="ps-rich">
    <Toolbar class="ps-rich__bar" :editor="editorRef" :default-config="toolbarConfig" mode="default" />
    <Editor
      v-model="html"
      class="ps-rich__body"
      :style="{ minHeight: minHeight + 'px' }"
      :default-config="editorConfig"
      mode="default"
      @on-created="handleCreated"
    />
  </div>
</template>

<style scoped>
.ps-rich {
  border: 1px solid var(--ps-card-border);
  border-radius: 8px;
  overflow: hidden;
  background: var(--n-color, #fff);
}

.ps-rich__bar {
  border-bottom: 1px solid var(--ps-card-border);
}

.ps-rich__body {
  overflow-y: auto;
  max-height: 50vh;
}
</style>
