## UI components: shadcn-vue

- Need a component? `cd web/app && pnpm dlx shadcn-vue@2.9.0 add <name>`, then `pnpm format`; it lands in `src/components/ui/<name>/`. Adapt it there and import it from its folder (`import { Button } from '@/components/ui/button'`).
{{~ if project["frontend.design-system"] == "none" ~}}
- Components style with the theme's variables (`bg-card`, `text-muted-foreground`); change colours and radius in `src/index.css`'s `:root` (light) and `.dark` (dark), never per component. The two palettes hold the same variables: change a colour in both, and swap a palette as a whole. `src/theme/contrast.test.ts` fails when a text colour has less than 4.5:1 on its background in either.
- The app follows the system's theme (`prefers-color-scheme`) until the user picks light, dark or system in `ThemeToggle.vue` (`src/theme/`), in the app's navigation. The choice is kept in `localStorage` under `theme`, and `index.html`'s script sets `.dark` on `<html>` before the first paint; change the key in both. Style what differs in the dark theme with the `dark:` variant or the palette, never by reading the theme in a component.
{{~ else ~}}
- Components style with the theme's variables (`bg-card`, `text-muted-foreground`); change colours and radius in `src/index.css`'s `:root`, never per component. The theme is light only: the design system's tokens set the brand colours (`primary`, `accent`, `border`, `muted-foreground`, `destructive`) with one value each, so a dark palette waits until those tokens have dark values.
{{~ end ~}}
{{~ if project["frontend.design-system"] == "tokens" || project["frontend.design-system"] == "web-components" ~}}
- A colour or radius the design system's tokens define (`primary`, `accent`, `border`, `rounded-md`, …) is changed in its token, not in `:root`: the tokens are imported after the theme and win.
{{~ end ~}}
- The CLI names files in one word (`Dialog.vue`); give each component a name in two with `defineOptions({ name: 'UiDialog' })`, as `Button.vue` does, or lint fails.
- Compose screens from those components; do not install PrimeVue, Vuetify, Quasar, Element Plus, Ant Design Vue, Naive UI or Headless UI (lint refuses them).
