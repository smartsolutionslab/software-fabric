## Frontend state: TanStack Query

- Server data: use the API client's query hooks (generated where the client generates them); never copy their data into `useState`, a context or a module variable.
- After a mutation, invalidate the queries it affects with `useQueryClient().invalidateQueries({ queryKey: ... })`, using the generated `get...QueryKey()` functions.
- Client state (UI, selections, drafts) is React state: `useState` / `useReducer`, a context when screens share it. No store library; lint refuses Redux, Zustand, MobX, Jotai, Recoil, Valtio, SWR and Apollo.
- A component test that renders a generated hook wraps it in `<QueryClientProvider client={new QueryClient()}>`.
