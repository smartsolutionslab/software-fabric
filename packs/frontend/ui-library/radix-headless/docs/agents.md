## UI components: Radix primitives

{{~ if project["frontend.styling"] == "css-modules" ~}}
- Need a component? Build it in `src/components/ui/` from a Radix primitive (`pnpm add @radix-ui/react-dialog --filter app`), the way `button.tsx` is: its styles in `button.module.css` beside it, variants with `cva` over the module's classes, classes joined with `cn` from `@/lib/utils`.
{{~ else ~}}
- Need a component? Build it in `src/components/ui/` from a Radix primitive (`pnpm add @radix-ui/react-dialog --filter app`), styled with Tailwind, the way `button.tsx` is: variants with `cva`, classes merged with `cn` from `@/lib/utils`. There is no theme: name Tailwind's palette colours (`bg-slate-900`, `text-red-600`).
{{~ end ~}}
- Give it a test next to it.
- Compose screens from those components; do not install MUI, Ant Design, Mantine, Chakra, Headless UI or React Aria (lint refuses them).
