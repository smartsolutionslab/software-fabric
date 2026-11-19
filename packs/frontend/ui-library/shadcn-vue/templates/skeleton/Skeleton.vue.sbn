<script setup lang="ts">
import type { HTMLAttributes } from 'vue';
import { cn } from '@/lib/utils';

// shadcn-vue names the file in one word; lint wants a component's name in two.
defineOptions({ name: 'UiSkeleton' });

interface SkeletonProps {
  class?: HTMLAttributes['class'];
}

const props = defineProps<SkeletonProps>();
</script>

<!-- The accent, as shadcn/ui's skeleton: the registry's primary at 10% would take the design system's primary token too. -->
<template>
  <div data-slot="skeleton" :class="cn('motion-safe:animate-pulse rounded-md bg-accent', props.class)" />
</template>
