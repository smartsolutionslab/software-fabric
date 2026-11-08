## UI components: shadcn-vue

- Need a component? `cd web/app && pnpm dlx shadcn-vue@2.9.0 add <name>`, then `pnpm format`; it lands in `src/components/ui/<name>/`. Adapt it there and import it from its folder (`import { Button } from '@/components/ui/button'`).
- Components style with the theme's variables (`bg-card`, `text-muted-foreground`); change colours and radius in `src/index.css`'s `:root`, never per component. The theme is light only; dark mode is a `.dark` block of the same variables.
{{~ if project["frontend.design-system"] == "tokens" ~}}
- A colour or radius the design system's tokens define (`primary`, `accent`, `border`, `rounded-md`, …) is changed in its token, not in `:root`: the tokens are imported after the theme and win.
{{~ end ~}}
- The CLI names files in one word (`Dialog.vue`); give each component a name in two with `defineOptions({ name: 'UiDialog' })`, as `Button.vue` does, or lint fails.
- Compose screens from those components; do not install PrimeVue, Vuetify, Quasar, Element Plus, Ant Design Vue, Naive UI or Headless UI (lint refuses them).
