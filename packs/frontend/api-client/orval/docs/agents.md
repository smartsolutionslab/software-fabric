## API client: orval

- Never write `fetch` to the backend; use the generated hooks and functions from `@/api/generated` (`useGetBillingNotes`, `usePostBillingNotes`, `getBillingNotes`, ...).
- After changing an endpoint: the integration tests rewrite `web/app/openapi.json`; commit it, run `pnpm codegen` in `web/app`, then adapt the screens. CI fails while the document is stale.
- Do not edit `src/api/generated.ts`; it is regenerated before every dev, build and test. Shared request behaviour (headers, errors) goes in `apiFetch` in `src/api/baseApi.ts`.
- A failed call rejects with `ApiError` (`status`, `message`); read it from the hook's `error`.
