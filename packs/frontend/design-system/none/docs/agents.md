## Design system: none

- Change colours, radii and spacing in one place ({{ if project["frontend.styling"] == "library-theme" }}the theme in `src/theme.ts`{{ else if project["frontend.ui-library"] == "vuetify" }}Vuetify's theme, created in `src/vuetify.ts`{{ else }}`src/index.css` theme variables{{ end }}), not per component.
