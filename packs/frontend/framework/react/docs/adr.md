# Frontend framework: React with Vite and TypeScript

**Status:** Accepted (generated)

## Decision

React 19, built with Vite and TypeScript in strict mode, linted with typescript-eslint's strict rules, tested with Vitest and Testing Library. `@/` resolves to `web/app/src`. Aspire runs the app in development (`AddViteApp`) alongside the backend.

TypeScript is pinned to the 6.x line: typescript-eslint does not support TypeScript 7 yet. Move to 7 when it does.

## Consequences

- Type errors fail the build (`tsc -b` runs before `vite build`), lint errors fail CI.
