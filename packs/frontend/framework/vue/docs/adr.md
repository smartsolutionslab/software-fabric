# Frontend framework: Vue with Vite and TypeScript

**Status:** Accepted (generated)

## Decision

Vue 3 with single-file components and the Composition API (`<script setup lang="ts">`), built with Vite and type-checked with `vue-tsc` in strict mode, linted with eslint-plugin-vue's essential rules and typescript-eslint's strict ones (`@vue/eslint-config-typescript`), formatted with Prettier, tested with Vitest and Testing Library (`@testing-library/vue`). `@/` resolves to `web/app/src`. Aspire runs the app in development (`AddViteApp`) alongside the backend.

TypeScript is pinned to the 6.x line: typescript-eslint does not support TypeScript 7 yet. Move to 7 when it does.

Every library is installed in `src/main.ts` by a provider, `(app, root) => Component`: it installs itself into the app (`app.use(router)`) and returns the root component, as it came or wrapped (a sign-in gate around everything). Providers are listed outermost first and applied from the innermost; the root renders at mount, once all of them have run.

Tests wait for a condition, never for a count. A fixed number of timer yields, or a settle right before an assertion, passes on the author's machine and fails on a loaded CI runner. A test waits with `findBy*` or `waitFor`, which poll the condition against a deadline. Driving fake timers forward (`vi.advanceTimersByTimeAsync`) is not waiting and stays allowed.

Playwright runs end-to-end tests in `web/app/e2e` against the running application (`pnpm e2e` after starting the AppHost). In development the Vite server answers on http://localhost:5173 and forwards `/api/*` to the host, so the browser talks to one origin.

## Consequences

- Type errors fail the build (`vue-tsc -b` runs before `vite build`, `.vue` files included), lint errors fail CI.
- `skipLibCheck` skips every declaration file, so after `vue-tsc -b` the build runs `check-declarations.mjs`: every package of the workspace whose `tsconfig.json` includes a declaration file (the app, a remote, a package) is checked again by `vue-tsc` with `skipLibCheck` off, and only errors outside `node_modules` fail the build. Declaration files under `node_modules` stay skipped; their errors are not ours to fix.
- eslint-plugin-vue's `recommended` and `strongly-recommended` sets are left out: beyond `essential` they are mostly layout rules (attributes per line, indentation) that Prettier owns and would contradict.
- Components are named in more than one word (`vue/multi-word-component-names`): a module's page is `BillingPage.vue`; the start page keeps its file name `Home.vue` and is named `HomePage` with `defineOptions`.
- Formatting is Prettier's alone (`.prettierrc.json`: single quotes, trailing commas, 2 spaces, 120 columns); CI runs `pnpm format:check` and fails on any file Prettier would change. The ESLint rules are correctness rules only; files a tool writes are listed in `.prettierignore`.
- In `*.test.ts`, ESLint (`no-restricted-syntax`) fails on a loop that awaits a `new Promise(...)` and on a settle (an awaited `new Promise(...)` or a helper named `settle`, `sleep`, `delay`, `wait`, `pause`, `tick`, `nextTick`, `flushPromises` or `setTimeout`) immediately before a statement calling `expect`. The rule is necessary, not sufficient: a settle inside a wrapper, two statements before the assertion, or in a `while` loop is for review. Never delete or narrow the rule; where a counted loop is genuinely the need, disable it for that line with the reason.
- Vitest runs without globals, so the test setup registers Testing Library's `cleanup` after each test itself.
