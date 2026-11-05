{{~ if project["frontend.framework"] == "vue" ~}}
## Styling: Tailwind

- Style with utility classes in `class`; merge conditional classes with `cn(...)` from `@/lib/utils`, or Vue's own `:class` binding.
- No CSS modules, no `<style>` blocks or inline `style` for layout; global CSS only in `src/index.css`.
{{~ else ~}}
## Styling: Tailwind

- Style with utility classes in `className`; merge conditional classes with `cn(...)`.
- No CSS modules, no inline `style` for layout; global CSS only in `src/index.css`.
{{~ end ~}}
