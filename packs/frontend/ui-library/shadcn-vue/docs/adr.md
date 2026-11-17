# UI components: shadcn-vue

**Status:** Accepted (generated)

## Decision

Components are shadcn-vue: Reka UI's primitives (MIT) styled with Tailwind, copied into `src/components/ui/` and owned by the project, as shadcn/ui is for React. Add one with `pnpm dlx shadcn-vue@2.9.0 add <component>` in `web/app`; `components.json` configures where it lands. The theme for `components.json`'s base colour (slate) lives in `src/index.css`: CSS variables mapped into Tailwind with `@theme inline`, plus `tw-animate-css` for the components' animations. Each component is a folder (`button/`) whose `index.ts` exports it with its variants; the button is the registry's new-york-v4 button (variants default, destructive, outline, secondary, ghost, link; sizes default, sm, lg, icon), so components the CLI adds on top of it type-check. No other component library. PrimeVue was the earlier choice and was dropped: from version 5 it is no longer MIT.
{{~ if project["frontend.design-system"] == "none" ~}}

The theme has two palettes of the same variables, shadcn's slate: `:root` is the light one and `.dark` the dark one, each swapped as a whole. The app follows the system's `prefers-color-scheme` until the user chooses light, dark or system with the theme toggle in its navigation (`src/theme/`); the choice is remembered in `localStorage` (a blocked storage only forgets it), and a script in `index.html` sets `.dark` on `<html>` before the first paint, so a dark page never flashes light. jsdom computes no colours, so axe in the component tests cannot judge contrast: `src/theme/contrast.test.ts` checks every text colour the components put on a background against WCAG AA (4.5:1) in both palettes, and with the accessibility tests each screen's axe check runs in both themes for what else differs.
{{~ end ~}}
{{~ if project["frontend.design-system"] == "tokens" || project["frontend.design-system"] == "web-components" ~}}

The design system's tokens are imported after the theme, so a colour or radius they define (`primary`, `accent`, `border`, `rounded-md`, …) is theirs; the theme supplies the rest (`card`, `muted`, `destructive`, …). The tokens have one value each, so the theme stays light only until they have dark values: a dark palette under light brand colours would fail contrast (`muted-foreground` on a dark background).
{{~ end ~}}
