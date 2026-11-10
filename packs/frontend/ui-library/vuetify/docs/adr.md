# UI components: Vuetify

**Status:** Accepted (generated)

## Decision

Screens are built from Vuetify's components (`vuetify` 4, MIT licensed): `VBtn` for buttons and `VTable` for plain tables, imported where they are used (`import { VBtn } from 'vuetify/components'`), and laid out with Vuetify's utility classes (`d-flex`, `ga-2`, `pa-4`, `text-body-medium`, `text-medium-emphasis`). They are not copied into the project; they come from the package and take their look from Vuetify's theme. `src/vuetify.ts` creates the one Vuetify instance; `src/main.ts` installs it outside every other provider and renders the app inside `VApp`, so the sign-in and every page take its theme, and `src/test-setup.ts` installs the same instance for every component test. `vuetify/styles` sets the page's baseline. Form fields stay labelled native controls styled with those classes, so the form library binds them as they are. No other component library, and no headless one beside it: a second set of components would bring a second look to keep in step.
{{~ if project["frontend.styling"] == "library-theme" ~}}

The theme is `src/theme.ts` (see the styling ADR): colours are changed there, never per component.
{{~ else ~}}

There is no theme of Vuetify's here (`frontend.styling` is {{ project["frontend.styling"] }}), so its components use Vuetify's default theme; the screens use Vuetify's classes, not {{ if project["frontend.styling"] == "tailwind" }}Tailwind's{{ else }}CSS Modules{{ end }}.
{{~ end ~}}
