# Design system: tokens

**Status:** Accepted (generated)

## Decision

The visual language's values are the project's own design tokens, in the workspace package `web/packages/tokens`: colours, radii and spacing in W3C DTCG JSON (`$value`, `$type`), compiled by Style Dictionary 5. They are the one place these values are set; a token is changed once, in its JSON file.
{{~ if project["frontend.styling"] == "tailwind" ~}}

The build writes every token as a CSS variable (`dist/tokens.css`, `--ds-color-primary`) and the same tokens as a Tailwind theme (`dist/theme.css`), which `src/index.css` imports, so `bg-primary`, `rounded-md` and `p-4` take the tokens' values.
{{~ else if project["frontend.styling"] == "library-theme" ~}}

The build also writes the tokens as a typed TypeScript module (`dist/tokens.ts`: `tokens.color.primary`, `tokens.radius.md`), the package's main export. The UI library's theme in `src/theme.ts` is built from it instead of from values of its own, so a token changed in its JSON file changes the theme and every component that takes it.
{{~ else ~}}

The build writes every token as a CSS custom property on `:root` (`dist/tokens.css`: `--ds-color-primary`, `--ds-radius-md`, `--ds-spacing-4`), which `src/index.css` imports, so every CSS module reads them with `var(...)`.
{{~ end ~}}
{{~ if project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue" ~}}

The UI library's theme stays in `src/index.css` for the colours the tokens do not name (`card`, `muted`, `destructive`, …); the tokens are imported after it, so a value they define is theirs.
{{~ end ~}}
{{~ if object.has_key project "frontend.color-scheme" ~}}

With the colour scheme, {{ project["frontend.color-scheme"] }}, the tokens take its brand shades and an accent of its own (`color.signal.500`), so that a primary and an accent element never look alike: the accent is at least 60° of OKLCH hue from the primary in both themes. {{ if project["frontend.color-scheme"] == "orange" }}It is sky, `#00a6f4`, 155° from the light primary (`color.brand.700`, `#ca3500`) and 177° from the dark one (`color.brand.400`, `#ff8904`); amber, the other schemes' accent, is 23° and 1° from them. Under its foreground, `color.ink.900`, it has 6.6:1.{{ else if project["frontend.color-scheme"] == "green" }}It is amber, `#d97706`, 91° from the light primary (`color.brand.700`, `#008236`) and 94° from the dark one (`color.brand.400`, `#05df72`). Under its foreground, `color.ink.900`, it has 5.6:1.{{ else }}It is amber, `#d97706`, 154° from the light primary (`color.brand.700`, `#1447e6`) and 164° from the dark one (`color.brand.400`, `#51a2ff`). Under its foreground, `color.ink.900`, it has 5.6:1.{{ end }} The dark primary is the scheme's 400 tone under `color.ink.900`, not its 800 tone under `color.ink.50`, because the primary is also text on the page (the link button) and no tone has 4.5:1 both on the dark background and under a light foreground: {{ if project["frontend.color-scheme"] == "orange" }}`#ff8904` has 8.5:1 on the dark background, where the 800 tone, `#9f2d00`, has 2.7:1, and 7.5:1 under `color.ink.900`{{ else if project["frontend.color-scheme"] == "green" }}`#05df72` has 11.3:1 on the dark background, where the 800 tone, `#016630`, has 2.8:1, and 10.0:1 under `color.ink.900`{{ else }}`#51a2ff` has 7.6:1 on the dark background, where the 800 tone, `#193cb8`, has 2.3:1, and 6.8:1 under `color.ink.900`{{ end }}. The contrast test in `src/theme/contrast.test.ts` checks both, with the light primary as text on the light background too.
{{~ end ~}}

There is no component package: the tokens serve the app's components, whatever framework renders them.

## Consequences

- The app reads the tokens' {{ if project["frontend.styling"] == "library-theme" }}TypeScript module{{ else }}CSS{{ end }}, so the tokens are built first: the app's `dev`, `build` and `test` build them, and a fresh clone needs no prior `pnpm build`.
- A token that references one that does not exist fails the build.
{{~ if project["frontend.styling"] == "tailwind" ~}}
- Tailwind's own scales stay available; a utility for a step the tokens do not define (`p-5`, `bg-red-500`) takes Tailwind's value, not a token's. Add the token when the step is part of the design.
{{~ end ~}}
