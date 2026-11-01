# Frontend framework: React with Vite and TypeScript

**Status:** Accepted (generated)

## Decision

React 19, built with Vite and TypeScript in strict mode, linted with typescript-eslint's strict rules, formatted with Prettier, tested with Vitest and Testing Library. `@/` resolves to `web/app/src`. Aspire runs the app in development (`AddViteApp`) alongside the backend.

TypeScript is pinned to the 6.x line: typescript-eslint does not support TypeScript 7 yet. Move to 7 when it does.

Tests wait for a condition, never for a count. A fixed number of timer yields, or a settle right before an assertion, passes on the author's machine (the fastest the test will ever run on) and fails on a loaded CI runner. A test waits with `findBy*` or `waitFor`, which poll the condition against a deadline: the deadline bounds failure, not the wait. Driving fake timers forward (`vi.advanceTimersByTimeAsync`) is not waiting and stays allowed.

Playwright runs end-to-end tests in `web/app/e2e` against the running application (`pnpm e2e` after starting the AppHost). In development the Vite server answers on http://localhost:5173 and forwards `/api/*` to the host, so the browser talks to one origin.

## Consequences

- Type errors fail the build (`tsc -b` runs before `vite build`), lint errors fail CI.
- Formatting is Prettier's alone (`.prettierrc.json`: single quotes, trailing commas, 2 spaces, 120 columns); CI runs `pnpm format:check` and fails on any file Prettier would change. The ESLint rules are correctness rules only, so the two never disagree; files a tool writes (the generated client, `openapi.json`) are listed in `.prettierignore`.
- In `*.test.ts(x)`, ESLint (`no-restricted-syntax`) fails on two shapes: a `for`, `for...of` or `for...in` loop that awaits a `new Promise(...)`, whatever its bound; and, immediately before a statement calling `expect`, an awaited `new Promise(...)`, an awaited call to a helper named `settle`, `sleep`, `delay`, `wait`, `pause`, `tick`, `nextTick`, `flushPromises` or `setTimeout`, or an empty `await act(async () => {})`.
- That rule is necessary, not sufficient. It cannot see:
  - a settle inside a wrapper (`await goLive()` where `goLive` sleeps), or a helper under any name outside the list above: the list is closed, a reserved-name guard, not a bound;
  - a settle two or more statements before the assertion it guards;
  - a `while` or `for (;;)` loop of timer yields, which has the shape of a genuine poll;
  - a poll whose condition is already true on entry, so it never waits: a settle that cannot fail, in the sanctioned idiom's clothes;
  - anything under `e2e/`: Playwright's `*.spec.ts` are outside the rule and wait with Playwright's own retrying assertions.

  Reviewers keep those. The loop selector cannot tell a timer promise from an event promise (`img.onload = resolve`), or driving fakes from synchronising an assertion, and a settle before `await waitFor(() => expect(...))` is flagged too (the settle adds nothing the poll does not; delete it); where a counted loop is genuinely the need, disable the rule for that line with the reason in the comment. Never delete or narrow the rule instead: a false positive costs one comment, a false negative a red CI nobody can reproduce.
