<script setup lang="ts">
import { Primitive, type PrimitiveProps } from 'reka-ui';
import type { HTMLAttributes } from 'vue';
import { cn } from '@/lib/utils';
import { buttonVariants, type ButtonVariants } from '.';

interface ButtonProps extends PrimitiveProps {
  variant?: ButtonVariants['variant'];
  size?: ButtonVariants['size'];
  class?: HTMLAttributes['class'];
}

// shadcn-vue names the file in one word; lint wants a component's name in two.
defineOptions({ name: 'UiButton' });

/** A button styled with Tailwind; as-child gives the look to its only child (a link, a router-link) instead. */
const { as = 'button', asChild = false, variant, size, class: className } = defineProps<ButtonProps>();
</script>

<template>
  <Primitive data-slot="button" :as="as" :as-child="asChild" :class="cn(buttonVariants({ variant, size }), className)">
    <slot />
  </Primitive>
</template>
