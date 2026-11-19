## Screenshot tests

- `web/app/e2e/<module>-page.screenshot.ts` compares each module's page in the light and the dark theme, showing its `<Module>Page.data.ts` examples; `fabric add-module` writes it, and its first run fails until you write its baselines with `pnpm screenshots:update`. A screen whose look matters gets a `toHaveScreenshot` test in `web/app/e2e/<screen>.screenshot.ts` too, with fixed data from `page.route`. They run only in Playwright's pinned container (`pnpm screenshots`), never with the host's browser.
- Update baselines with the AppHost running: `pnpm screenshots:update` in `web/app`. Look at every changed image and commit it with the change that caused it.
- Update a baseline only when a change you meant alters how a page looks: the page or its data file's examples, a component, the theme, the font, or Playwright's version. A difference you did not mean is a regression to fix; read the expected, actual and diff images first, in `web/app/test-results/screenshots` or, from CI, the `screenshot-diffs` artifact.
- Never delete a baseline, raise the threshold in `e2e/screenshots.config.ts` or skip the test to get past a difference; mask content that changes on its own with `mask: [locator]`.
- `@playwright/test` and the image in `.github/scripts/screenshots.sh` change together; the script refuses a mismatch.
