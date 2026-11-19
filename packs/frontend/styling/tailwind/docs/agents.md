{{~ if project["frontend.framework"] == "vue" ~}}
## Styling: Tailwind

- Style with utility classes in `class`; merge conditional classes with `cn(...)` from `@/lib/utils`, or Vue's own `:class` binding.
- No CSS modules, no `<style>` blocks or inline `style` for layout; global CSS only in `src/index.css`.
- An animation runs only without reduced motion: every `animate-*` and `transition*` class carries `motion-safe:` (`motion-safe:transition-colors`, `motion-safe:data-[state=open]:animate-in`), also in a component a CLI adds. Lint fails one without it (`motion/respect-reduced-motion`); never turn the rule off.
{{~ else ~}}
## Styling: Tailwind

- Style with utility classes in `className`; merge conditional classes with `cn(...)`.
- No CSS modules, no inline `style` for layout; global CSS only in `src/index.css`.
- An animation runs only without reduced motion: every `animate-*` and `transition*` class carries `motion-safe:` (`motion-safe:transition-colors`, `motion-safe:data-[state=open]:animate-in`), also in a component a CLI adds. Lint fails one without it (`motion/respect-reduced-motion`); never turn the rule off.
{{~ end ~}}
