# Test extra: screenshot tests

**Status:** Accepted (generated)

## Decision

Playwright's `toHaveScreenshot` compares pages of the running application with baselines committed in this
repository. `e2e/start-page.screenshot.ts` covers the start page, and `fabric add-module` gives each module page that
starts from the sample `e2e/<module>-page.screenshot.ts`, which compares it in the light and the dark theme. A screen
whose look matters gets its own `*.screenshot.ts` beside them.

- **Where the baselines live:** `web/app/e2e/__screenshots__/<test file>/<name>.png`, committed with the change that
  caused them. The path has no platform or browser in it, because only one environment renders them.
- **How they stay the same on every machine:** `.github/scripts/screenshots.sh` runs the screenshot tests in
  Playwright's own image (`mcr.microsoft.com/playwright`, Ubuntu Noble, pinned by digest and run as `linux/amd64`), so
  the browser build, the fonts and the OS are the same for a developer and for CI. The host's Playwright, which runs
  the other end-to-end tests, never runs them: their config (`e2e/screenshots.config.ts`) is separate, and the
  end-to-end config picks up only `*.spec.ts`. The image's version is `@playwright/test`'s; the script refuses to run
  when they differ, so update both together.
- **How a page renders the same every time:**
  - *Data:* a module page's test answers its list request with the `examples` of its `<Module>Page.data.ts`, the data
    the page's axe check renders it with (without the accessibility tests, the same example written in the test), so
    the samples the end-to-end tests create never reach a screenshot.
  - *Theme:* the test sets the system's colour scheme (`page.emulateMedia`) before the first page loads, and the app
    follows it as it does for a user who has not chosen one.
  - *Motion:* `e2e/screenshots.config.ts` stops animations and hides the caret before every comparison.
  - *Fonts:* the font comes from its npm package with the app, and the test waits for `document.fonts.ready`.
- **How different is different:** a comparison fails when more than 100 pixels differ by more than Playwright's
  default colour distance (0.2), stated once in `e2e/screenshots.config.ts`. One renderer makes every image, so the
  allowance is for an anti-aliased edge, not for a word or a colour anyone would see.
- **When they run:** `.github/scripts/e2e.sh` runs them after the end-to-end tests, against the same running
  application, so the e2e job fails on a changed screenshot.
- **How a developer updates them:** start the application (`dotnet run --project src/AppHost`), then
  `pnpm screenshots:update` in `web/app`. It rewrites only the baselines that changed; review the images and commit them.
  `pnpm screenshots` compares without writing.
- **What a failure leaves:** Playwright writes the baseline, what rendered and their difference (`*-diff.png`) under
  `web/app/test-results/screenshots`; on a failed e2e job, CI uploads them as the artifact `screenshot-diffs`.

## Consequences

- A new project has no baselines, and the generator writes none (a rendered image depends on the project). Until
  some are committed, a run writes them, compares nothing and prints a warning; commit them from
  `pnpm screenshots:update` to arm the gate. A screenshot test added later without its baseline fails.
- Docker with host networking is required: the browser in the container reaches the application and the identity
  provider at `localhost`. On macOS that means Docker Desktop with host networking turned on; on Apple silicon the
  `linux/amd64` image runs emulated and slower, which is the price of one rendering for everyone.
- Content that changes on its own (times, counters, generated data) is masked (`mask: [locator]`) or answered with
  fixed data, never given a larger tolerance.
- A module page whose look depends on its sample stops matching when the sample is replaced: route the page's own
  requests to its data file's examples in its screenshot test, then update the baselines. Pages without the sample
  (other UI libraries, micro-frontends, the dashboards and assistant pages) get no generated screenshot test.
