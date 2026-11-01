# Frontend state: TanStack Query

**Status:** Accepted (generated)

## Decision

Server data lives in TanStack Query's cache, one `QueryClient` provided around the app (`src/providers/query.tsx`). Components read it with query hooks and change it with mutations (the API client's generated ones where it generates them); there is no store library. What is not server data (an open dialog, a selection, a draft) is React state: `useState`, `useReducer`, or a context when several screens share it.

## Consequences

- After a mutation, invalidate the queries it changes (`queryClient.invalidateQueries({ queryKey: getGetBillingNotesQueryKey() })`) instead of patching a copy of the data.
- Component tests that render a screen using a generated hook wrap it in a `QueryClientProvider` with a fresh `QueryClient`.
- The gate is a lint rule: importing a store library (Redux, Zustand, MobX, Jotai, Recoil, Valtio) or another server-data cache (SWR, Apollo) fails `pnpm lint`. It sees imports only. It does not see server data copied into `useState` or a context, nor a cache that is hand-written; those are for review.
