# Test extra: Storybook tests

**Status:** Accepted (generated)

## Decision

Every story of the design system is a test. `web/packages/ui/src/stories.test.tsx` finds the story files in `src/` by glob, composes each story with the project's Storybook annotations (`.storybook/preview.ts`) through Storybook's portable stories (`composeStories`), renders it and runs its play function (`story.run()`). It runs in the ui package's Vitest, in jsdom, with `pnpm test`, so CI needs no browser and no running Storybook for it.

- A story that throws while it renders fails the test, and so does a play function whose assertion fails; the ui package's build already fails on a story that does not compile.
- Stories are the design system's components in `@{{ name }}/ui`, the package that has Storybook. The app's pages and the UI library's components in `web/app` have no stories.
- Storybook's test runner needs a served Storybook and a browser, and its Vitest addon runs in Vitest's browser mode; both would need Playwright's Chromium in the frontend job before `pnpm test`. jsdom checks what a story renders and does, not how it looks: comparing pixels is a screenshot test's job.

## Consequences

- A new story is tested without a line in the test file.
- Interactions are play functions with `expect`, `within` and `userEvent` from `storybook/test`; the same function runs in Storybook's interactions panel and in CI.
- The stories import the tokens' CSS through `.storybook/preview.ts`, so the tokens are built before the ui package's tests: its `test` script builds them first, so the tests pass on a fresh clone without `pnpm build`.
