## Frontend state: Pinia

- All state is in Pinia stores in `src/stores/`, one per concern, written with `defineStore` in setup style: the client state screens share and the server data they show. State one component needs stays in that component.
- A store loads server data in an action through {{ if project["frontend.api-client"] == "openapi-fetch" }}`api` (`(await api.GET('/billing/notes')).data`){{ else }}the generated request functions (`await getBillingNotes()`){{ end }} and keeps its loading and error state beside it. Screens read the store; they do not call the API themselves.
- After a change, the action that made it reloads what it changed, or updates the store from the answer. Never keep server data in a component `ref` or a module variable as well.
- Lint refuses TanStack Query, Pinia Colada, swrv, Apollo, Vuex and MobX.
- A test that uses a store installs Pinia (`setActivePinia(createPinia())`, or `global: { plugins: [createPinia()] }` when mounting) and stubs `fetch`.
