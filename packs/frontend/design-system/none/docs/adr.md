# Design system: none

**Status:** Accepted (generated)

## Decision

No separate design system: the UI library's components and {{ if project["frontend.styling"] == "library-theme" }}its theme (`src/theme.ts`){{ else if project["frontend.ui-library"] == "vuetify" }}Vuetify's default theme{{ else }}Tailwind's theme{{ end }} are the visual language. Introduce design tokens when a second application or a brand requirement needs them.
