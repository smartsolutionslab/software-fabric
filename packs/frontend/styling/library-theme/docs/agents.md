## Styling: the library's theme

{{~ if project["frontend.ui-library"] == "mui" ~}}
{{~ if project["frontend.design-system"] == "tokens" || project["frontend.design-system"] == "web-components" ~}}
- Colours, shape and spacing live in one place, the design tokens, which the MUI theme in `src/theme.ts` is built from; change them in their token, never in the theme or per component.
{{~ else ~}}
- Colours, shape and type live in one place, the MUI theme in `src/theme.ts`; change them there, never per component.
{{~ end ~}}
- Style with MUI's props and `sx`, naming the theme's values (`color="error"`; `bgcolor: 'action.hover'` and `p: 1` in `sx`). No CSS files, no `className`, no inline `style`; `src/index.css` only imports the stylesheets a library needs.
- A component that writes a colour of its own (`#hex`, `rgb()`) anywhere in its code fails `src/theme.test.tsx`; text, comments and the values of content keys and attributes (`label`, `title`, `href`, `to`, `alt`, `description`, `text`, `placeholder`, `aria-*`) may look like one (`Issue #393`, `href="#add"`). Never style with a content value.
{{~ if project["frontend.routing"] == "react-router" ~}}
- A link to a route is MUI's `Link` drawn as the router's: `<Link component={NavLink} to="/path">`, so it takes the theme's colours.
{{~ else if project["frontend.routing"] == "tanstack-router" ~}}
- A link to a route is MUI's `Link` made a router link once with `createLink(Link)` from `@tanstack/react-router`, so it takes the theme's colours.
{{~ end ~}}
{{~ else if project["frontend.ui-library"] == "vuetify" ~}}
{{~ if project["frontend.design-system"] == "tokens" || project["frontend.design-system"] == "web-components" ~}}
- Colours live in one place, the design tokens, which the Vuetify theme in `src/theme.ts` is built from; change them in their token, never in the theme or per component.
{{~ else ~}}
- Colours live in one place, the Vuetify theme in `src/theme.ts`; change them there, never per component.
{{~ end ~}}
- Style with Vuetify's props and utility classes, naming the theme's colours (`color="error"`, `class="text-medium-emphasis pa-2"`). No CSS files, no `<style>` blocks, no inline `style`; `src/index.css` only imports the stylesheets a library needs.
- A component that writes a colour of its own (`#hex`, `rgb()`) anywhere in its template, script or style fails `src/theme.test.ts`; text, comments and the values of content keys and attributes, written or bound (`label`, `title`, `href`, `to`, `alt`, `description`, `text`, `placeholder`, `aria-*`), may look like one (`Issue #393`, `href="#add"`). Never style with a content value.
{{~ else ~}}
- The theme for {{ project["frontend.ui-library"] }} is not built yet: `src/theme.ts` throws and `src/theme.test.ts` fails until the generator builds it.
{{~ end ~}}
