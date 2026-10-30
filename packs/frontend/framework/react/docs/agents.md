## Frontend framework: React

- Components are function components in `.tsx`; one component per file, named like the file.
- Import from `@/…` (= `web/app/src`), never with long relative paths.
- Tests sit next to the code (`Thing.test.tsx`), use Testing Library queries by role and label, and import `test`/`expect` from `vitest`.
- No `any`, no non-null assertions (`!`); lint fails on both.
