# UI components: Reka UI with Tailwind

**Status:** Accepted (generated)

## Decision

Components are built on Reka UI's headless primitives (MIT), styled with Tailwind, and live in `src/components/ui/`, owned by the project: the Vue counterpart of Radix with shadcn/ui. `BaseButton.vue` is the first; others follow its shape (a primitive from `reka-ui`, variants with `class-variance-authority`, classes merged with `cn` from `src/lib/utils.ts`). No other component library. PrimeVue was the earlier choice and was dropped: from version 5 it is no longer MIT.
