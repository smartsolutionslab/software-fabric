## Design system: tokens and components

- Colours and radii are tokens in `web/packages/tokens/src/*.tokens.json` (W3C DTCG: `$value`, `$type`; reference another token as `{color.brand.700}`). Use them through Tailwind (`bg-primary`, `rounded-md`) or as CSS variables (`var(--ds-color-primary)`); never write a colour value in a component.
- The design system's own components live in `web/packages/ui/src/`, one per file, each with `<Name>.stories.tsx` and `<Name>.test.tsx`, exported from `src/index.ts`. The app imports them from `@{{ name }}/ui`, never from a file inside the package.
{{~ if project["frontend.ui-library"] == "shadcn" }}
- shadcn components stay in `web/app/src/components/ui/` for what only the app needs; a component shared in one look across screens goes into `@{{ name }}/ui`.
{{~ end }}
- Browse the components with `pnpm --filter @{{ name }}/ui storybook` in `web/`; `pnpm build` builds Storybook, so a story that does not compile fails CI.
