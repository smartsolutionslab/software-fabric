# Design system: tokens and components

**Status:** Accepted (generated)

## Decision

The visual language is the project's own, in two workspace packages:

- `web/packages/tokens`: design tokens in W3C DTCG JSON (`$value`, `$type`), compiled by Style Dictionary 5 to CSS variables (`dist/tokens.css`, `--ds-color-primary`) and to a Tailwind theme (`dist/theme.css`), which the app's stylesheet imports, so `bg-primary` and `rounded-md` take the tokens' values. A token is changed once, in its JSON file.
- `web/packages/ui` (`@{{ name }}/ui`): the design system's own React components, built on the tokens, each with a Storybook story and a test. Its build type-checks them and builds Storybook, so a broken story fails CI.
{{~ if project["frontend.ui-library"] == "shadcn" }}

shadcn/ui stays in the app (`web/app/src/components/ui/`) for components only the app needs; a component every screen shares in one look belongs in `@{{ name }}/ui`.
{{~ end }}
{{~ if object.has_key project "frontend.color-scheme" ~}}

With the colour scheme, {{ project["frontend.color-scheme"] }}, the tokens take its brand shades and an accent of its own (`color.signal.500`), so that a primary and an accent element never look alike: the accent is at least 60° of OKLCH hue from the primary in both themes. {{ if project["frontend.color-scheme"] == "orange" }}It is sky, `#00a6f4`, 155° from the light primary (`color.brand.700`, `#ca3500`) and 177° from the dark one (`color.brand.400`, `#ff8904`); amber, the other schemes' accent, is 23° and 1° from them. Under its foreground, `color.ink.900`, it has 6.6:1.{{ else if project["frontend.color-scheme"] == "green" }}It is amber, `#d97706`, 91° from the light primary (`color.brand.700`, `#008236`) and 94° from the dark one (`color.brand.400`, `#05df72`). Under its foreground, `color.ink.900`, it has 5.6:1.{{ else }}It is amber, `#d97706`, 154° from the light primary (`color.brand.700`, `#1447e6`) and 164° from the dark one (`color.brand.400`, `#51a2ff`). Under its foreground, `color.ink.900`, it has 5.6:1.{{ end }} The dark primary is the scheme's 400 tone under `color.ink.900`, not its 800 tone under `color.ink.50`, because the primary is also text on the page (the link button) and no tone has 4.5:1 both on the dark background and under a light foreground: {{ if project["frontend.color-scheme"] == "orange" }}`#ff8904` has 8.5:1 on the dark background, where the 800 tone, `#9f2d00`, has 2.7:1, and 7.5:1 under `color.ink.900`{{ else if project["frontend.color-scheme"] == "green" }}`#05df72` has 11.3:1 on the dark background, where the 800 tone, `#016630`, has 2.8:1, and 10.0:1 under `color.ink.900`{{ else }}`#51a2ff` has 7.6:1 on the dark background, where the 800 tone, `#193cb8`, has 2.3:1, and 6.8:1 under `color.ink.900`{{ end }}. The contrast test in `src/theme/contrast.test.ts` checks both, with the light primary as text on the light background too.
{{~ end ~}}

## Consequences

- The app and the ui package's Storybook read the tokens' CSS, so the tokens are built before them: the app's `dev`, `build` and `test` and the ui package's `storybook` and `test` build them first, and a fresh clone needs no prior `pnpm build`.
- A token that references one that does not exist fails the build.
