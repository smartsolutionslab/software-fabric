## Frontend state: Zustand and TanStack Query

- Server data: use the API client's query hooks (generated where the client generates them); never copy their data into a Zustand store, `useState` or a module variable, and never fetch inside a store's action.
- After a mutation, invalidate the queries it affects with `useQueryClient().invalidateQueries({ queryKey: ... })`, using the generated `get...QueryKey()` functions.
- Client state shared by screens (UI, selections, drafts) is a Zustand store in `src/stores/`, one per concern, created with `create<State>()(...)`; read it through its hook with a selector (`useSelection((state) => state.noteId)`). State one component needs stays in that component. Lint refuses Redux, MobX, Jotai, Recoil, Valtio, SWR and Apollo.
- A component test that renders a generated hook wraps it in `<QueryClientProvider client={new QueryClient()}>`; a test that changes a store resets it with `setState` afterwards.
