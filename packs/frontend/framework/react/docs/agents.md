## Frontend framework: React

- Components are function components in `.tsx`; one component per file, named like the file.
- Import from `@/…` (= `web/app/src`), never with long relative paths.
- Tests sit next to the code (`Thing.test.tsx`), use Testing Library queries by role and label, and import `test`/`expect` from `vitest`.
- Call the backend through `/api/...`; the dev server forwards it to the host. Never hard-code the host's port.
- End-to-end tests go in `e2e/` (Playwright); run them with the AppHost up: `pnpm e2e`.
- No `any`, no non-null assertions (`!`); lint fails on both.
