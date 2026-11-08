## API client: openapi-fetch

- Never write `fetch` to the backend; call it through `api` from `@/api/baseApi`, with the method and path the document names: `api.GET('/billing/notes')`, `api.POST('/billing/notes', { body })`, `api.GET('/billing/notes/{id}', { params: { path: { id } } })`.
{{ if project["frontend.state"] == "pinia" }}- Call it from a Pinia store's action and keep the answer in the store: `notes.value = (await api.GET('/billing/notes')).data`.{{ else }}- In a screen, wrap a call in TanStack Query: `useQuery({ queryKey: ['/billing/notes'], queryFn: async () => (await api.GET('/billing/notes')).data })`. The key is the path, then its parameters.{{ end }}
- After changing an endpoint: the integration tests rewrite `web/app/openapi.json`; commit it, run `pnpm codegen` in `web/app`, then adapt the screens. CI fails while the document is stale.
- Do not edit `src/api/generated.ts`; it is regenerated before every dev, build and test. Shared request behaviour (headers, errors) goes in `src/api/baseApi.ts`.
- A failed call rejects with `ApiError` (`status`, `message`); {{ if project["frontend.state"] == "pinia" }}catch it in the store action and keep it as the store's error{{ else }}read it from the query's `error`{{ end }}.
