<script setup lang="ts">
import OrderPage from '../OrderPage.vue'
import { salesKind } from '../orderKind'

/** 销售订单：页面本体与采购/销售共用（见 ../OrderPage.vue），这里只注入差异描述符。 */
</script>

<template>
  <OrderPage :kind="salesKind" />
</template>
