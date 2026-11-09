# Styling: the UI library's theme

**Status:** Accepted (generated)

## Decision

{{~ if project["frontend.ui-library"] == "mui" ~}}
The app is styled by MUI's own theming system, with no Tailwind and no CSS framework beside it. The theme object, `createTheme(...)`, is one generated file, `src/theme.ts`: palette, shape and type for every component. `src/main.tsx` puts it around the whole app with `ThemeProvider`, outside every other provider, so the sign-in and every page take it. Components take colours, spacing and radii from it by name (`color="text.secondary"`, `bgcolor: 'primary.main'` and `p: 1` in `sx`), never as values of their own; `src/theme.test.tsx` fails the web tests when a component (`*.tsx`) writes a colour, naming it.

The theme is light only. Dark mode is a second palette in the same file (`colorSchemes: { dark: ... }`), not a change in components.
{{~ else ~}}
The app is to be styled by the UI library's own theming system, its theme object in one generated file, `src/theme.ts`. The generator builds that theme for MUI only so far; for `frontend.ui-library` {{ project["frontend.ui-library"] }}, `src/theme.ts` throws and `src/theme.test.ts` fails the web tests saying so, until it builds this one.
{{~ end ~}}
