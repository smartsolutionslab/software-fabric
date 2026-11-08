## UI components: shadcn/ui

- Need a component? `cd web/app && pnpm dlx shadcn@latest add <name>`, then `pnpm format`; it lands in `src/components/ui/`. Adapt it there.
- Components style with the theme's variables (`bg-card`, `text-muted-foreground`); change colours and radius in `src/index.css`'s `:root`, never per component. The theme is light only; dark mode is a `.dark` block of the same variables.
{{~ if project["frontend.design-system"] == "tokens-and-components" || project["frontend.design-system"] == "tokens" ~}}
- A colour or radius the design system's tokens define (`primary`, `accent`, `border`, `rounded-md`, …) is changed in its token, not in `:root`: the tokens are imported after the theme and win.
{{~ end ~}}
- Compose screens from those components; do not install MUI, Ant Design, Mantine or Chakra (lint refuses them).
