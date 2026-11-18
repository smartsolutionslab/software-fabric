## Font: Inter

- The app's typeface is Inter, `fabric.yaml`'s font (`frontend.font`): the npm package `@fontsource-variable/inter`, imported in `src/index.css` and bundled with the app, never from a font host. Never add a `<link>` or an `@import` of Google Fonts or another CDN: `src/theme/font.test.ts` fails on anything `index.html` or `src/index.css` loads from another host.
- The theme's `--font-sans` names it; text takes it through `font-sans` (the page's default) and monospace text the system's through `font-mono`, never a `font-family` of a component's own. Another font is a change of the project's choices (Inter and Geist are offered), decided with a decision record of its own, not a hand edit.
