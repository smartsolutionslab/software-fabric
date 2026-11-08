# UI components: shadcn/ui

**Status:** Accepted (generated)

## Decision

Components are shadcn/ui: Radix primitives styled with Tailwind, copied into `src/components/ui/` and owned by the project. Add one with `pnpm dlx shadcn@latest add <component>` in `web/app`; `components.json` configures where it lands. The theme for `components.json`'s base colour (slate) lives in `src/index.css`: CSS variables mapped into Tailwind with `@theme inline`, plus `tw-animate-css` for the components' animations. The button is the registry's new-york-v4 button (variants default, destructive, outline, secondary, ghost, link; sizes default, sm, lg, icon), so components the CLI adds on top of it type-check. No other component library.
{{~ if project["frontend.design-system"] == "tokens-and-components" || project["frontend.design-system"] == "tokens" ~}}

The design system's tokens are imported after the theme, so a colour or radius they define (`primary`, `accent`, `border`, `rounded-md`, …) is theirs; the theme supplies the rest (`card`, `muted`, `destructive`, …).
{{~ end ~}}
