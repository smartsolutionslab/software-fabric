## Screenshot tests

- A screen whose look matters gets a `toHaveScreenshot` test in `web/app/e2e/<screen>.screenshot.ts`. It runs only in Playwright's pinned container (`pnpm screenshots`), never with the host's browser.
- Update baselines with the AppHost running: `pnpm screenshots:update` in `web/app`. Look at every changed image and commit it with the change that caused it.
- Never delete a baseline, raise a threshold or skip the test to get past a difference; mask content that changes on its own with `mask: [locator]`.
- `@playwright/test` and the image in `.github/scripts/screenshots.sh` change together; the script refuses a mismatch.
