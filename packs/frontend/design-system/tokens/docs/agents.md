## Design system: tokens

- Colours, radii and spacing are tokens in `web/packages/tokens/src/*.tokens.json` (W3C DTCG: `$value`, `$type`; reference another token as `{color.brand.700}`). Change a value there, never in a component.
{{~ if project["frontend.styling"] == "tailwind" ~}}
- Use them through Tailwind's utilities (`bg-primary`, `rounded-md`, `p-4`) or as CSS variables (`var(--ds-color-primary)`). Use only the steps the tokens define; a step you need that is missing (`p-5`) becomes a token first.
{{~ else if project["frontend.styling"] == "library-theme" ~}}
- The app uses them through the library's theme in `src/theme.ts`, which is built from the compiled tokens (`import { tokens } from '@{{ name }}/tokens'`, `tokens.color.primary`); a screen names the theme's values, never a token's. A value the theme needs that is missing becomes a token first.
{{~ else ~}}
- Use them in CSS modules as custom properties: `color: var(--ds-color-primary)`, `border-radius: var(--ds-radius-md)`, `padding: var(--ds-spacing-4)`. A step you need that is missing becomes a token first.
{{~ end ~}}
- Never write a colour value, radius or spacing length in a component's styles.
- `pnpm build` compiles the tokens; a reference to a token that does not exist fails it.
