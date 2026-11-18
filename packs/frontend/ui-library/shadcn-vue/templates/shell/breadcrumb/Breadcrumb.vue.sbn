<script lang="ts" setup>
import type { HTMLAttributes } from 'vue';

// shadcn-vue names the file in one word; lint wants a component's name in two.
defineOptions({ name: 'UiBreadcrumb' });

const props = defineProps<{
  class?: HTMLAttributes['class'];
}>();
</script>

<template>
  <nav aria-label="breadcrumb" data-slot="breadcrumb" :class="props.class">
    <slot />
  </nav>
</template>
