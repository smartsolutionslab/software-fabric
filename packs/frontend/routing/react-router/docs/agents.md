## Routing: React Router

- A module's screens live in `src/modules/<module>/`; its `routes.tsx` exports `route` and the app picks it up by itself. Do not list modules in `App.tsx`.
- Import from `react-router`, never `react-router-dom`.
- Tests render routes inside `MemoryRouter`.
