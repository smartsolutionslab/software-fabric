<script setup lang="ts">
// What a list shows when it has nothing to show: an icon (the `icon` slot, `<Inbox />` from @lucide/vue), one sentence
// saying what is missing (the default slot) and the primary action that fills the list (the `action` slot, such as a
// `<Button>`). The icon is decoration, so screen readers skip it.
defineSlots<{ icon: () => unknown; default: () => unknown; action?: () => unknown }>();
</script>

<template>
  <div data-slot="empty-state" class="flex flex-col items-center gap-3 py-8 text-center">
    <div aria-hidden="true" class="text-muted-foreground">
      <slot name="icon" />
    </div>
    <p class="text-body text-muted-foreground">
      <slot />
    </p>
    <slot name="action" />
  </div>
</template>
