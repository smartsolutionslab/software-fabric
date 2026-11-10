## Design system: web components

- Colours, radii and spacing are tokens in `web/packages/tokens/src/*.tokens.json` (W3C DTCG: `$value`, `$type`; reference another token as `{color.brand.700}`). Change a value there, never in a component.
{{~ if project["frontend.styling"] == "tailwind" ~}}
- In the app, use them through Tailwind's utilities (`bg-primary`, `rounded-md`, `p-4`) or as CSS variables (`var(--ds-color-primary)`).
{{~ else if project["frontend.styling"] == "library-theme" ~}}
- In the app, use them through the library's theme in `src/theme.ts`, which is built from the compiled tokens (`import { tokens } from '@{{ name }}/tokens'`, `tokens.color.primary`); a screen names the theme's values, never a token's.
{{~ else ~}}
- In the app, use them in CSS modules as custom properties: `color: var(--ds-color-primary)`, `padding: var(--ds-spacing-4)`.
{{~ end ~}}
- The design system's components are Lit web components in `web/packages/ui/src/`, one per file (`Badge.ts` defines `<ds-badge>`), each with `<Name>.stories.ts` and `<Name>.test.ts`, exported from `src/index.ts`. Every tag starts with `ds-`.
- A component's styles live in its shadow root (``static styles = css`...` ``) and use only the tokens' variables (`var(--ds-color-accent)`, `var(--ds-radius-sm)`); every colour a component paints is a `--ds-color-*` token's (no literal, no named colour, no variable of its own), and no Tailwind classes, which do not reach inside it. The ui package's tests check the colours Chromium computes for every variant, so a new tone needs its tokens in the test's `tones`.
- A reactive property is declared in `static properties` and with `declare` in the class, then set in the constructor; a class field would hide it.
- Screens use the elements as tags (`<ds-badge tone="primary">New</ds-badge>`); the app's entry imports `@{{ name }}/ui` once, so never import a component file from a screen.
{{~ if project["frontend.framework"] == "react" ~}}
- A new element gets its JSX entry in `web/app/src/design-system.d.ts`{{ if project["frontend.architecture"] == "micro-frontends" }} and in every remote's `src/design-system.d.ts`{{ end }}, with the properties it takes, or the screens that use it do not type-check.
{{~ end ~}}
- Browse the components with `pnpm --filter @{{ name }}/ui storybook` in `web/`; `pnpm build` builds Storybook, so a story that does not compile fails CI.
