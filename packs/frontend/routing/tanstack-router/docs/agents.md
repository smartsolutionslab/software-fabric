## Routing: TanStack Router

- {{ if project["frontend.architecture"] == "micro-frontends" }}A module's route lives in the shell, `src/modules/<module>/`: its `routes.tsx` exports `route`, the app picks it up by itself, and its page mounts the module's remote; the screens themselves live in the remote, `web/modules/<module>`.{{ else }}A module's screens live in `src/modules/<module>/`; its `routes.tsx` exports `route` and the app picks it up by itself.{{ end }} Do not list modules in `App.tsx` or `src/router.tsx`.
- Link with `<Link>`, render the matched page with `<Outlet />`; both are imported from `@tanstack/react-router`, never `react-router`. Routes are declared in code, not with the file-based router plugin.
- Tests create the router with `createAppRouter(createMemoryHistory(), <App />)` from `@/router`, render `<RouterProvider router={router} />`, and wait for the page with `findBy*`.
