<script lang="ts" setup>
import type { HTMLAttributes } from 'vue';
import { cn } from '@/lib/utils';

const props = defineProps<{
  class?: HTMLAttributes['class'];
}>();
</script>

<!-- Text, not the registry's disabled role="link": the page's own link (the sidebar's) stays the only one of its name. -->
<template>
  <span data-slot="breadcrumb-page" aria-current="page" :class="cn('text-foreground font-normal', props.class)">
    <slot />
  </span>
</template>
