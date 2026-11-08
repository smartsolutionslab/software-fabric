## API client: orval

- Never write `fetch` to the backend; use the generated {{ if project["frontend.framework"] == "vue" && project["frontend.state"] == "pinia" }}functions from `@/api/generated` (`getBillingNotes`, `postBillingNotes`, ...), called from a store's actions{{ else }}hooks and functions from `@/api/generated` (`useGetBillingNotes`, `usePostBillingNotes`, `getBillingNotes`, ...){{ end }}.
- After changing an endpoint: the integration tests rewrite `web/app/openapi.json`; commit it, run `pnpm codegen` in `web/app`, then adapt the screens. CI fails while the document is stale.
- Do not edit `src/api/generated.ts`; it is regenerated before every dev, build and test. Shared request behaviour (headers, errors) goes in `apiFetch` in `src/api/baseApi.ts`.
- A failed call rejects with `ApiError` (`status`, `message`); {{ if project["frontend.framework"] == "vue" && project["frontend.state"] == "pinia" }}catch it in the store action and keep it as the store's error{{ else }}read it from the hook's `error`{{ end }}.
