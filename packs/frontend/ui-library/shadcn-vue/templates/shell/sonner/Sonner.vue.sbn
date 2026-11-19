<script lang="ts" setup>
import type { ToasterProps } from 'vue-sonner';
import { CircleCheckIcon, InfoIcon, Loader2Icon, OctagonXIcon, TriangleAlertIcon, XIcon } from '@lucide/vue';
import { cn } from '@/lib/utils';
import { Toaster as Sonner } from 'vue-sonner';

// shadcn-vue names the file in one word; lint wants a component's name in two.
defineOptions({ name: 'UiSonner' });

const props = defineProps<ToasterProps>();
</script>

<!-- Slots named in full (v-slot:): the generator's tests read a hash before "error" as a branch a pack does not support. -->
<template>
  <Sonner
    :class="cn('toaster group', props.class)"
    :style="{
      '--normal-bg': 'var(--popover)',
      '--normal-text': 'var(--popover-foreground)',
      '--normal-border': 'var(--border)',
      '--border-radius': 'var(--radius)',
    }"
    v-bind="props"
    :toast-options="{ ...props.toastOptions, style: { boxShadow: 'var(--elevation-3)' } }"
  >
    <template v-slot:success-icon>
      <CircleCheckIcon class="size-4" />
    </template>
    <template v-slot:info-icon>
      <InfoIcon class="size-4" />
    </template>
    <template v-slot:warning-icon>
      <TriangleAlertIcon class="size-4" />
    </template>
    <template v-slot:error-icon>
      <OctagonXIcon class="size-4" />
    </template>
    <template v-slot:loading-icon>
      <div>
        <Loader2Icon class="size-4 motion-safe:animate-spin" />
      </div>
    </template>
    <template v-slot:close-icon>
      <XIcon class="size-4" />
    </template>
  </Sonner>
</template>
