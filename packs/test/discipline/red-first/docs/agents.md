## Test discipline: red first

- New behaviour: write the test first, run it, and see it fail for the expected reason. Quote that output in the PR under `## Tests observed failing`.
- A refactor: run the covering tests before the change and quote them passing; they must pass unmodified after it. A refactor with no covering test starts by writing one.
- A test that arrives green on new behaviour is a failed step, not a shortcut.
- CI rejects a PR touching `src/` or `web/` without that section.
