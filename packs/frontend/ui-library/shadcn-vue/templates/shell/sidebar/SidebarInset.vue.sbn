<script setup lang="ts">
import type { HTMLAttributes } from 'vue';
import { cn } from '@/lib/utils';

const props = defineProps<{
  class?: HTMLAttributes['class'];
}>();
</script>

<!-- A div, not the registry's main: every page of the app renders its own <main>, and a page has only one. -->
<template>
  <div
    data-slot="sidebar-inset"
    :class="
      cn(
        'bg-background relative flex w-full flex-1 flex-col',
        'md:peer-data-[variant=inset]:m-2 md:peer-data-[variant=inset]:ml-0 md:peer-data-[variant=inset]:rounded-xl md:peer-data-[variant=inset]:shadow-elevation-1 md:peer-data-[variant=inset]:peer-data-[state=collapsed]:ml-2',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
