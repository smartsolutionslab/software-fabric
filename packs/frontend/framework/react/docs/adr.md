# Frontend framework: React with Vite and TypeScript

**Status:** Accepted (generated)

## Decision

React 19, built with Vite and TypeScript in strict mode, linted with typescript-eslint's strict rules, formatted with Prettier, tested with Vitest and Testing Library. `@/` resolves to `web/app/src`. Aspire runs the app in development (`AddViteApp`) alongside the backend.

TypeScript is pinned to the 6.x line: typescript-eslint does not support TypeScript 7 yet. Move to 7 when it does.

Playwright runs end-to-end tests in `web/app/e2e` against the running application (`pnpm e2e` after starting the AppHost). In development the Vite server answers on http://localhost:5173 and forwards `/api/*` to the host, so the browser talks to one origin.

## Consequences

- Type errors fail the build (`tsc -b` runs before `vite build`), lint errors fail CI.
- Formatting is Prettier's alone (`.prettierrc.json`: single quotes, trailing commas, 2 spaces, 120 columns); CI runs `pnpm format:check` and fails on any file Prettier would change. The ESLint rules are correctness rules only, so the two never disagree; files a tool writes (the generated client, `openapi.json`) are listed in `.prettierignore`.
