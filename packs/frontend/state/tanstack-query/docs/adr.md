{{~ if project["frontend.framework"] == "vue" ~}}
# Frontend state: TanStack Query

**Status:** Accepted (generated)

## Decision

Server data lives in TanStack Query's cache (`@tanstack/vue-query`, one `QueryClient` installed as a plugin in `src/providers/query.ts`). Components read it with query hooks and change it with mutations (the API client's generated ones where it generates them); there is no store library. What is not server data (an open dialog, a selection, a draft) is Vue state: a `ref` in the component, provided and injected when several screens share it.

## Consequences

- After a mutation, invalidate the queries it changes ({{ if project["frontend.api-client"] == "openapi-fetch" }}`queryClient.invalidateQueries({ queryKey: ['/billing/notes'] })`{{ else }}`queryClient.invalidateQueries({ queryKey: getGetBillingNotesQueryKey() })`{{ end }}) instead of patching a copy of the data.
- Component tests that render a screen using a generated hook install `VueQueryPlugin` with a fresh `QueryClient`.
- The gate is a lint rule: importing a store library (Pinia, Vuex, MobX) or another server-data cache (swrv, Apollo) fails `pnpm lint`. It sees imports only. It does not see server data copied into a `ref` or a provided value, nor a cache that is hand-written; those are for review.
{{~ else ~}}
# Frontend state: TanStack Query

**Status:** Accepted (generated)

## Decision

Server data lives in TanStack Query's cache, one `QueryClient` provided around the app (`src/providers/query.tsx`). Components read it with query hooks and change it with mutations (the API client's generated ones where it generates them); there is no store library. What is not server data (an open dialog, a selection, a draft) is React state: `useState`, `useReducer`, or a context when several screens share it.

## Consequences

- After a mutation, invalidate the queries it changes ({{ if project["frontend.api-client"] == "openapi-fetch" }}`queryClient.invalidateQueries({ queryKey: ['/billing/notes'] })`{{ else }}`queryClient.invalidateQueries({ queryKey: getGetBillingNotesQueryKey() })`{{ end }}) instead of patching a copy of the data.
- Component tests that render a screen using a generated hook wrap it in a `QueryClientProvider` with a fresh `QueryClient`.
- The gate is a lint rule: importing a store library (Redux, Zustand, MobX, Jotai, Recoil, Valtio) or another server-data cache (SWR, Apollo) fails `pnpm lint`. It sees imports only. It does not see server data copied into `useState` or a context, nor a cache that is hand-written; those are for review.
{{~ end ~}}