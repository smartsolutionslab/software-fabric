## Styling: the library's theme

{{~ if project["frontend.ui-library"] == "mui" ~}}
- Colours, shape and type live in one place, the MUI theme in `src/theme.ts`; change them there, never per component.
- Style with MUI's props and `sx`, naming the theme's values (`color="error"`; `bgcolor: 'action.hover'` and `p: 1` in `sx`). No CSS files, no `className`, no inline `style`; `src/index.css` only imports the stylesheets a library needs.
- A component that writes a colour of its own (`#hex`, `rgb()`) fails `src/theme.test.tsx`.
{{~ else ~}}
- The theme for {{ project["frontend.ui-library"] }} is not built yet: `src/theme.ts` throws and `src/theme.test.ts` fails until the generator builds it.
{{~ end ~}}
