# Design system: tokens

**Status:** Accepted (generated)

## Decision

The visual language's values are the project's own design tokens, in the workspace package `web/packages/tokens`: colours, radii and spacing in W3C DTCG JSON (`$value`, `$type`), compiled by Style Dictionary 5. They are the one place these values are set; a token is changed once, in its JSON file.
{{~ if project["frontend.styling"] == "tailwind" ~}}

The build writes every token as a CSS variable (`dist/tokens.css`, `--ds-color-primary`) and the same tokens as a Tailwind theme (`dist/theme.css`), which `src/index.css` imports, so `bg-primary`, `rounded-md` and `p-4` take the tokens' values.
{{~ else ~}}

The build writes every token as a CSS custom property on `:root` (`dist/tokens.css`: `--ds-color-primary`, `--ds-radius-md`, `--ds-spacing-4`), which `src/index.css` imports, so every CSS module reads them with `var(...)`.
{{~ end ~}}
{{~ if project["frontend.ui-library"] == "shadcn" || project["frontend.ui-library"] == "shadcn-vue" ~}}

The UI library's theme stays in `src/index.css` for the colours the tokens do not name (`card`, `muted`, `destructive`, …); the tokens are imported after it, so a value they define is theirs.
{{~ end ~}}

There is no component package: the tokens serve the app's components, whatever framework renders them.

## Consequences

- The app reads the tokens' CSS, so the tokens are built first: the app's `dev`, `build` and `test` build them, and a fresh clone needs no prior `pnpm build`.
- A token that references one that does not exist fails the build.
{{~ if project["frontend.styling"] == "tailwind" ~}}
- Tailwind's own scales stay available; a utility for a step the tokens do not define (`p-5`, `bg-red-500`) takes Tailwind's value, not a token's. Add the token when the step is part of the design.
{{~ end ~}}
