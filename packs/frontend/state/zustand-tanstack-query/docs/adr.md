# Frontend state: Zustand and TanStack Query

**Status:** Accepted (generated)

## Decision

Two kinds of state, two places. Server data lives in TanStack Query's cache (`@tanstack/react-query`, one `QueryClient` provided around the app in `src/providers/query.tsx`); components read it with query hooks and change it with mutations, the API client's generated ones where it generates them. Client state that several screens share (an open dialog, a selection, a draft) lives in Zustand stores (`src/stores/`), each created with `create` and read through its own hook; a store needs no provider. State one component needs stays in it, with `useState` or `useReducer`.

## Consequences

- A store never holds a copy of server data: after a mutation, invalidate the queries it changes (`queryClient.invalidateQueries({ queryKey: getGetBillingNotesQueryKey() })`) instead of patching a copy in a store.
- A store is a module-level singleton, so tests that change one reset it (`useSelection.setState(initial)`) before the next test. Component tests that render a screen using a generated hook wrap it in a `QueryClientProvider` with a fresh `QueryClient`.
- The gate is a lint rule: importing another store library (Redux, MobX, Jotai, Recoil, Valtio) or another server-data cache (SWR, Apollo) fails `pnpm lint`. It sees imports only. It does not see server data copied into a store, nor a fetch inside a store's action; those are for review.
