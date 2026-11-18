<script setup lang="ts">
import type { DialogRootEmits, DialogRootProps } from 'reka-ui';
import { DialogRoot, useForwardPropsEmits } from 'reka-ui';

// shadcn-vue names the file in one word; lint wants a component's name in two.
defineOptions({ name: 'UiSheet' });

const props = defineProps<DialogRootProps>();
const emits = defineEmits<DialogRootEmits>();

const forwarded = useForwardPropsEmits(props, emits);
</script>

<template>
  <DialogRoot v-slot="slotProps" data-slot="sheet" v-bind="forwarded">
    <slot v-bind="slotProps" />
  </DialogRoot>
</template>
