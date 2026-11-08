## Frontend state: Pinia and TanStack Query

- Server data: use the API client's query hooks (generated where the client generates them); never copy their data into a Pinia store, a `ref` or a module variable.
- After a mutation, invalidate the queries it affects with `useQueryClient().invalidateQueries({ queryKey: ... })`{{ if project["frontend.api-client"] == "openapi-fetch" }}, using the key each query was made with (`['/billing/notes']`).{{ else }}, using the generated `get...QueryKey()` functions.{{ end }}
- Client state shared by screens (UI, selections, drafts) is a Pinia store in `src/stores/`, one per concern, written with `defineStore` in setup style; state one component needs stays in that component. Lint refuses Vuex, MobX, swrv, Apollo and Pinia Colada.
- A component test that renders a generated hook installs `VueQueryPlugin` (`global: { plugins: [VueQueryPlugin] }`); one that uses a store installs `createPinia()` the same way.
