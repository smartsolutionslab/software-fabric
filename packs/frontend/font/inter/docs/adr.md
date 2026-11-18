# Font: Inter

**Status:** Accepted (generated)

## Decision

The app's typeface is Inter, a variable font under the SIL Open Font License 1.1, installed from npm as `@fontsource-variable/inter` (Fontsource) and bundled with the app: `src/index.css` imports the package's stylesheet, whose `@font-face` rules name the package's `woff2` files, and the build copies those files into the app. The browser fetches the font from the app's own origin, never from a font host such as Google Fonts or another CDN, so no visitor's address reaches a third party and the app depends on no host it does not run. The theme's `--font-sans` names `'Inter Variable'` first, ahead of the system's fonts, which show while it loads (`font-display: swap`); monospace text keeps the system's monospace fonts. `src/theme/font.test.ts` fails when `index.html` or `src/index.css` loads anything from another host, or when a face of the package is not a file of the package.

## Consequences

- The font is a choice recorded in `fabric.yaml` (`frontend.font`), made when the project is generated; Inter and Geist are offered. `fabric upgrade` keeps the choice, so it brings this font's changes in a newer generator, not another font.
- The package's version is pinned in `web/app/package.json` like every other dependency. Every script it covers (Latin, Latin Extended, Cyrillic, Greek, Vietnamese) is bundled; a browser downloads only the files whose `unicode-range` a page's text uses.
