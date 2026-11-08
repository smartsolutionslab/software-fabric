{{~ css_modules = project["frontend.styling"] == "css-modules" ~}}
# UI components: Reka UI with {{ if css_modules }}CSS Modules{{ else }}Tailwind{{ end }}

**Status:** Accepted (generated)

## Decision

{{~ if css_modules ~}}
Components are built on Reka UI's headless primitives (MIT), styled with CSS Modules, and live in `src/components/ui/`, owned by the project. `BaseButton.vue` is the first; others follow its shape (a primitive from `reka-ui`, its styles in a `.module.css` beside it, variants with `class-variance-authority` over the module's classes, classes joined with `cn` from `src/lib/utils.ts`). No other component library. PrimeVue was the earlier choice and was dropped: from version 5 it is no longer MIT.
{{~ else ~}}
Components are built on Reka UI's headless primitives (MIT), styled with Tailwind, and live in `src/components/ui/`, owned by the project: the Vue counterpart of Radix with shadcn/ui. `BaseButton.vue` is the first; others follow its shape (a primitive from `reka-ui`, variants with `class-variance-authority`, classes merged with `cn` from `src/lib/utils.ts`). No other component library. PrimeVue was the earlier choice and was dropped: from version 5 it is no longer MIT.
{{~ end ~}}
