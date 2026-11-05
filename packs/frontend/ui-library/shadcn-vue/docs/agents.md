## UI components: shadcn-vue

- Need a component? `cd web/app && pnpm dlx shadcn-vue@2.9.0 add <name>`, then `pnpm format`; it lands in `src/components/ui/<name>/`. Adapt it there and import it from its folder (`import { Button } from '@/components/ui/button'`).
- The CLI names files in one word (`Dialog.vue`); give each component a name in two with `defineOptions({ name: 'UiDialog' })`, as `Button.vue` does, or lint fails.
- Compose screens from those components; do not install PrimeVue, Vuetify, Quasar, Element Plus, Ant Design Vue, Naive UI or Headless UI (lint refuses them).
