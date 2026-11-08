# Frontend state: Pinia and TanStack Query

**Status:** Accepted (generated)

## Decision

Two kinds of state, two places. Server data lives in TanStack Query's cache (`@tanstack/vue-query`, one `QueryClient` installed in `src/providers/query.ts`); components read it with query hooks and change it with mutations, the API client's generated ones where it generates them. Client state that several screens share (an open dialog, a selection, a draft) lives in Pinia stores (`src/stores/`), installed in `src/providers/pinia.ts`; state one component needs stays in it.

## Consequences

- A store never holds a copy of server data: after a mutation, invalidate the queries it changes ({{ if project["frontend.api-client"] == "openapi-fetch" }}`queryClient.invalidateQueries({ queryKey: ['/billing/notes'] })`{{ else }}`queryClient.invalidateQueries({ queryKey: getGetBillingNotesQueryKey() })`{{ end }}).
- Component tests install the plugins they need: `VueQueryPlugin` for a screen using a generated hook, `createPinia()` for one using a store.
- The gate is a lint rule: importing another store library (Vuex, MobX) or another server-data cache (swrv, Apollo, Pinia Colada) fails `pnpm lint`. It sees imports only; server data copied into a store is for review.
