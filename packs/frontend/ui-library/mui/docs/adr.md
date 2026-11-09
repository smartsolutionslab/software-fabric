# UI components: MUI

**Status:** Accepted (generated)

## Decision

Screens are built from MUI's components (`@mui/material` 9, MIT licensed, styled with Emotion): `Button`, `Typography`, `Stack`, `Paper`, and `Box` for layout. They are not copied into the project; they come from the package and take their look from MUI's theme. `CssBaseline` sets the page's baseline (margins, background, font) from that theme in `src/main.tsx`. No other component library, and no headless one beside it: a second set of components would bring a second look to keep in step.
{{~ if project["frontend.styling"] == "library-theme" ~}}

The theme is `src/theme.ts` (see the styling ADR): colours, shape and type are changed there, never per component.
{{~ else ~}}

There is no theme object of MUI's here (`frontend.styling` is {{ project["frontend.styling"] }}), so its components use MUI's default theme.
{{~ end ~}}
