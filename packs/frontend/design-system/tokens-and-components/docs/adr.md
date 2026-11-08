# Design system: tokens and components

**Status:** Accepted (generated)

## Decision

The visual language is the project's own, in two workspace packages:

- `web/packages/tokens`: design tokens in W3C DTCG JSON (`$value`, `$type`), compiled by Style Dictionary 5 to CSS variables (`dist/tokens.css`, `--ds-color-primary`) and to a Tailwind theme (`dist/theme.css`), which the app's stylesheet imports, so `bg-primary` and `rounded-md` take the tokens' values. A token is changed once, in its JSON file.
- `web/packages/ui` (`@{{ name }}/ui`): the design system's own React components, built on the tokens, each with a Storybook story and a test. Its build type-checks them and builds Storybook, so a broken story fails CI.
{{~ if project["frontend.ui-library"] == "shadcn" }}

shadcn/ui stays in the app (`web/app/src/components/ui/`) for components only the app needs; a component every screen shares in one look belongs in `@{{ name }}/ui`.
{{~ end }}

## Consequences

- The app and the ui package's Storybook read the tokens' CSS, so the tokens are built before them: the app's `dev`, `build` and `test` and the ui package's `storybook` and `test` build them first, and a fresh clone needs no prior `pnpm build`.
- A token that references one that does not exist fails the build.
