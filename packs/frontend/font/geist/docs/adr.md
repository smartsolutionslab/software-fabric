# Font: Geist

**Status:** Accepted (generated)

## Decision

The app's typeface is Geist, with Geist Mono for monospace text (identifiers, codes): variable fonts under the SIL Open Font License 1.1, installed from npm as `@fontsource-variable/geist` and `@fontsource-variable/geist-mono` (Fontsource) and bundled with the app: `src/index.css` imports the packages' stylesheets, whose `@font-face` rules name the packages' `woff2` files, and the build copies those files into the app. The browser fetches the fonts from the app's own origin, never from a font host such as Google Fonts or another CDN, so no visitor's address reaches a third party and the app depends on no host it does not run. The theme's `--font-sans` names `'Geist Variable'` first and its `--font-mono` (`font-mono`, `code`, `kbd`, `pre`) `'Geist Mono Variable'`, each ahead of the system's fonts, which show while they load (`font-display: swap`). `src/theme/font.test.ts` fails when `index.html` or `src/index.css` loads anything from another host, or when a face of either package is not a file of its package.

## Consequences

- The font is a choice recorded in `fabric.yaml` (`frontend.font`); Inter and Geist are offered, and neither is edited in by hand.
- Another font is a change of that choice: `fabric change --set frontend.font=<font>` merges the other font's package and theme names into the project and records it, keeping the project's other edits. `fabric upgrade` keeps the recorded font and brings its changes in a newer generator.
- The packages' versions are pinned in `web/app/package.json` like every other dependency. Every script they cover is bundled; a browser downloads only the files whose `unicode-range` a page's text uses.
