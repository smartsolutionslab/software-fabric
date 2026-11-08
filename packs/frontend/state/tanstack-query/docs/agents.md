{{~ if project["frontend.framework"] == "vue" ~}}
## Frontend state: TanStack Query

- Server data: use the API client's query hooks (generated where the client generates them); never copy their data into a `ref`, a `reactive` or a module variable.
- After a mutation, invalidate the queries it affects with `useQueryClient().invalidateQueries({ queryKey: ... })`{{ if project["frontend.api-client"] == "openapi-fetch" }}, using the key each query was made with (`['/billing/notes']`).{{ else }}, using the generated `get...QueryKey()` functions.{{ end }}
- Client state (UI, selections, drafts) is Vue state: `ref` / `reactive` in the component, `provide` / `inject` when screens share it. No store library; lint refuses Pinia, Vuex, MobX, swrv and Apollo.
- A component test that renders a generated hook installs `VueQueryPlugin` (`global: { plugins: [VueQueryPlugin] }`).
{{~ else ~}}
## Frontend state: TanStack Query

- Server data: use the API client's query hooks (generated where the client generates them); never copy their data into `useState`, a context or a module variable.
- After a mutation, invalidate the queries it affects with `useQueryClient().invalidateQueries({ queryKey: ... })`{{ if project["frontend.api-client"] == "openapi-fetch" }}, using the key each query was made with (`['/billing/notes']`).{{ else }}, using the generated `get...QueryKey()` functions.{{ end }}
- Client state (UI, selections, drafts) is React state: `useState` / `useReducer`, a context when screens share it. No store library; lint refuses Redux, Zustand, MobX, Jotai, Recoil, Valtio, SWR and Apollo.
- A component test that renders a generated hook wraps it in `<QueryClientProvider client={new QueryClient()}>`.
{{~ end ~}}