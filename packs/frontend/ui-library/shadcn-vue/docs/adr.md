# UI components: shadcn-vue

**Status:** Accepted (generated)

## Decision

Components are shadcn-vue: Reka UI's primitives (MIT) styled with Tailwind, copied into `src/components/ui/` and owned by the project, as shadcn/ui is for React. Add one with `pnpm dlx shadcn-vue@2.9.0 add <component>` in `web/app`; `components.json` configures where it lands. Each component is a folder (`button/`) whose `index.ts` exports it with its variants. No other component library. PrimeVue was the earlier choice and was dropped: from version 5 it is no longer MIT.
