## Test discipline: red first

- New behaviour: write the test first, run it, and see it fail for the expected reason. Quote that output in the PR under `## Tests observed failing`.
- A refactor: run the covering tests before the change and quote them passing under `## Tests observed passing before the change`; they must pass unmodified after it. A refactor with no covering test starts by writing one. A test you have to edit to pass after a refactor means the behaviour moved: stop, do not adjust it.
- A test that arrives green on new behaviour is a failed step, not a shortcut.
- CI rejects a PR touching `src/` or `web/` without one of those sections quoting output in a code block. The one exception is a Dependabot PR that changes dependency manifests and lockfiles only, which passes with a note.
