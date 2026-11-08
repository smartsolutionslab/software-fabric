# Frontend state: Pinia

**Status:** Accepted (generated)

## Decision

One state library. Pinia stores (`src/stores/`, one per concern), installed in `src/providers/pinia.ts`, hold both the client state that several screens share (an open dialog, a selection, a draft) and the server data the screens show. There is no query cache: a store loads server data in an action through the API client ({{ if project["frontend.api-client"] == "openapi-fetch" }}`(await api.GET('/billing/notes')).data`{{ else }}the generated request functions, `await getBillingNotes()`{{ end }}) and keeps it, with its loading and error state, until an action loads it again. State one component needs stays in that component.

## Consequences

- Nothing decides for the store when its data is stale. The action that changes something on the server reloads what it changed, or updates the store from the answer; screens that show the same data read the same store, never a copy of their own.
- Store and component tests install Pinia (`setActivePinia(createPinia())`, or `global: { plugins: [createPinia()] }` for a mounted component) and stub `fetch`.
- The gate is a lint rule: importing a server-data cache (TanStack Query, Pinia Colada, swrv, Apollo) or another store library (Vuex, MobX) fails `pnpm lint`. It sees imports only; server data kept in a component's `ref` instead of a store is for review.
{{~ if object.has_key(project, "frontend.dashboards") && array.contains project["frontend.dashboards"] "user-editable" ~}}
- The dashboards' editor is built on TanStack Query hooks, so it is not generated here: the dashboards module and its API are, and a screen for them is a store and a page of the project's own.
{{~ end ~}}
