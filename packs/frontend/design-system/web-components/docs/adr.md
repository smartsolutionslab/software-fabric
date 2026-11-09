# Design system: web components

**Status:** Accepted (generated)

## Decision

The visual language is the project's own, in two workspace packages, and serves any framework that renders HTML:

- `web/packages/tokens`: colours, radii and spacing as design tokens in W3C DTCG JSON (`$value`, `$type`), compiled by Style Dictionary 5. The build writes every token as a CSS variable on `:root` (`dist/tokens.css`: `--ds-color-primary`, `--ds-radius-md`, `--ds-spacing-4`){{ if project["frontend.styling"] == "tailwind" }} and the same tokens as a Tailwind theme (`dist/theme.css`), which `src/index.css` imports, so `bg-primary`, `rounded-md` and `p-4` take the tokens' values{{ else }}, which `src/index.css` imports, so every CSS module reads them with `var(...)`{{ end }}. A token is changed once, in its JSON file.
- `web/packages/ui` (`@{{ name }}/ui`): the design system's own components as web components, written with Lit 3, each with a Storybook story (the web-components renderer) and a test. Its build type-checks them and builds Storybook, so a broken story fails CI. The app imports the package once, in `src/main.{{ if project["frontend.framework"] == "vue" }}ts{{ else }}tsx{{ end }}`, which defines the elements; screens use them as tags (`<ds-badge tone="primary">`).

The components render into shadow DOM: the page's styles, Tailwind's utilities included, do not reach inside them. They are styled only through the tokens' `--ds-*` variables, which custom properties inherit into the shadow root, so a token changed in its JSON file changes the components and the app alike.
{{~ if project["frontend.framework"] == "vue" ~}}

Vue's compiler is told to leave every `ds-` tag to the browser (`isCustomElement` in `vite.config.ts`), so it does not look for a Vue component of that name.
{{~ else ~}}

React renders a tag with a hyphen as a custom element and passes a property the element defines (`tone`) as that property. `src/design-system.d.ts` declares each element's tag for JSX, so a screen's use of it is type-checked.
{{~ end ~}}

## Consequences

- The same components serve a React and a Vue app; nothing in them depends on either.
- The app reads the tokens' CSS, so the tokens are built first: the app's `dev`, `build` and `test` and the ui package's `storybook` build them, and a fresh clone needs no prior `pnpm build`.
- A token that references one that does not exist fails the build; a component whose shadow styles carry a colour value of their own (a literal, a named colour such as `red` or `transparent`, or a variable that is not a colour token) fails the ui package's tests.
- A component cannot be restyled from outside except through the tokens or the `part`s it exposes (`::part(badge)`); that is the point of the design system, not a limitation to work around.
