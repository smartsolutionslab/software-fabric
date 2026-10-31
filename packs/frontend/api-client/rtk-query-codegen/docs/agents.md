## API client: RTK Query codegen

- Never write `fetch` to the backend; use the generated hooks from `@/api/generated` (`useGetBillingNotesQuery`, ...).
- After changing an endpoint: the integration tests rewrite `web/app/openapi.json`; commit it, run `pnpm codegen` in `web/app`, then adapt the screens. CI fails while the document is stale.
- Do not edit `src/api/generated.ts`; it is regenerated before every dev, build and test. Put shared request behaviour in `src/api/baseApi.ts`.
