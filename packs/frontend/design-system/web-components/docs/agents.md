## Design system: web components

- Colours, radii and spacing are tokens in `web/packages/tokens/src/*.tokens.json` (W3C DTCG: `$value`, `$type`; reference another token as `{color.brand.700}`). Change a value there, never in a component.
{{~ if project["frontend.styling"] == "tailwind" ~}}
- In the app, use them through Tailwind's utilities (`bg-primary`, `rounded-md`, `p-4`) or as CSS variables (`var(--ds-color-primary)`).
{{~ else ~}}
- In the app, use them in CSS modules as custom properties: `color: var(--ds-color-primary)`, `padding: var(--ds-spacing-4)`.
{{~ end ~}}
- The design system's components are Lit web components in `web/packages/ui/src/`, one per file (`Badge.ts` defines `<ds-badge>`), each with `<Name>.stories.ts` and `<Name>.test.ts`, exported from `src/index.ts`. Every tag starts with `ds-`.
- A component's styles live in its shadow root (``static styles = css`...` ``) and use only the tokens' variables (`var(--ds-color-accent)`, `var(--ds-radius-sm)`); a colour property takes a `--ds-color-*` token and nothing else (no literal, no named colour, no variable of its own), a colour inside `background`, `border`, `outline` or a shadow is such a token too, and no Tailwind classes, which do not reach inside it.
- A reactive property is declared in `static properties` and with `declare` in the class, then set in the constructor; a class field would hide it.
- Screens use the elements as tags (`<ds-badge tone="primary">New</ds-badge>`); the app's entry imports `@{{ name }}/ui` once, so never import a component file from a screen.
{{~ if project["frontend.framework"] == "react" ~}}
- A new element gets its JSX entry in `web/app/src/design-system.d.ts`, with the properties it takes, or the screens that use it do not type-check.
{{~ end ~}}
- Browse the components with `pnpm --filter @{{ name }}/ui storybook` in `web/`; `pnpm build` builds Storybook, so a story that does not compile fails CI.
