## UI components: Reka UI

- Need a component? Build it in `src/components/ui/` from a Reka UI primitive (`import { DialogRoot, ... } from 'reka-ui'`), styled with Tailwind, the way `BaseButton.vue` is: variants with `cva`, classes merged with `cn` from `@/lib/utils`.
- Name it in more than one word, `Base` first (`BaseDialog.vue`), and give it a test next to it.
- Compose screens from those components; do not install PrimeVue, Vuetify, Quasar, Element Plus, Ant Design Vue, Naive UI or Headless UI (lint refuses them).
