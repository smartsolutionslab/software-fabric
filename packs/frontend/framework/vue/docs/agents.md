## Frontend framework: Vue

- Components are single-file components (`.vue`) with `<script setup lang="ts">`, using the Composition API; one component per file, named like the file in more than one word (`BillingPage.vue`); lint fails on a one-word name.
- Import from `@/…` (= `web/app/src`), never with long relative paths.
- Tests sit next to the code (`Thing.test.ts`), render with `@testing-library/vue`, query by role and label, and import `test`/`expect` from `vitest`.
- A test waits for a condition, never a count: `await screen.findBy…(...)` or `await waitFor(() => expect(...))`. No loop of `await new Promise(...)`, no sleep right before `expect`; lint fails on both. Driving fake timers (`vi.advanceTimersByTimeAsync`) is fine.
- Call the backend through `/api/...`; the dev server forwards it to the host. Never hard-code the host's port.
- End-to-end tests go in `e2e/` (Playwright); run them with the AppHost up: `pnpm e2e`.
- No `any`, no non-null assertions (`!`); lint fails on both.
- A `.d.ts` file you write is type-checked by `pnpm build` (`check-declarations.mjs`), although `skipLibCheck` is on; fix its errors, never exclude it.
- A library is installed in `src/main.ts` through its pack's provider, never with `app.use` added by hand there.
- Run `pnpm format` in `web/app` before committing; CI runs `pnpm format:check` and fails on any file Prettier would change. Do not add formatting rules to ESLint.
- A file a tool writes (not people) goes in `.prettierignore`, never a hand-formatted copy.
