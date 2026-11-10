# Styling: the UI library's theme

**Status:** Accepted (generated)

## Decision

{{~ if project["frontend.ui-library"] == "mui" ~}}
The app is styled by MUI's own theming system, with no Tailwind and no CSS framework beside it. The theme object, `createTheme(...)`, is one generated file, `src/theme.ts`: palette, shape and type for every component. `src/main.tsx` puts it around the whole app with `ThemeProvider`, outside every other provider, so the sign-in and every page take it. Components take colours, spacing and radii from it by name (`color="text.secondary"`, `bgcolor: 'primary.main'` and `p: 1` in `sx`), never as values of their own; `src/theme.test.tsx` fails the web tests when a component (`*.tsx`) writes a colour, naming it. The navigation's router links are MUI's `Link`, so they take the theme's colours too.

{{ if project["frontend.design-system"] == "tokens" || project["frontend.design-system"] == "web-components" }}The theme's colours, radius and spacing are not its own: `src/theme.ts` builds them from the design tokens' TypeScript module (`import { tokens } from '@{{ name }}/tokens'`), so a token changed in its JSON file changes the theme and every component that takes it.

{{ end }}The theme is light only. `src/index.css` holds no rules of its own, only the stylesheets a library needs imported.
{{~ else if project["frontend.ui-library"] == "vuetify" ~}}
The app is styled by Vuetify's own theming system, with no Tailwind and no CSS framework beside it. The theme, Vuetify's `theme` option, is one generated file, `src/theme.ts`: the colours of its light theme. `src/vuetify.ts` creates Vuetify with it, and `src/main.ts` installs that instance outside every other provider and renders the app inside `VApp`, so the sign-in and every page take it. Vuetify writes the theme's colours as CSS variables (`--v-theme-primary`) and classes (`bg-primary`, `text-error`); components take colours from it by name (`color="primary"`, `class="text-medium-emphasis"`), never as values of their own. `src/theme.test.ts` fails the web tests when Vuetify does not take the theme, or when a component (`*.vue`) writes a colour, naming it.

{{ if project["frontend.design-system"] == "tokens" || project["frontend.design-system"] == "web-components" }}The theme's colours are not its own: `src/theme.ts` builds them from the design tokens' TypeScript module (`import { tokens } from '@{{ name }}/tokens'`), so a token changed in its JSON file changes the theme and every component that takes it.

{{ end }}The theme is light only. `src/index.css` holds no rules of its own, only the stylesheets a library needs imported.
{{~ else ~}}
The app is to be styled by the UI library's own theming system, its theme object in one generated file, `src/theme.ts`. The generator builds that theme for MUI and Vuetify only so far; for `frontend.ui-library` {{ project["frontend.ui-library"] }}, `src/theme.ts` throws and `src/theme.test.ts` fails the web tests saying so, until it builds this one.
{{~ end ~}}
