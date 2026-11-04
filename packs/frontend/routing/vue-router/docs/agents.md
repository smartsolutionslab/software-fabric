## Routing: Vue Router

- A module's screens live in `src/modules/<module>/`; its `routes.ts` exports `route` and the app picks it up by itself. Do not list modules in `App.vue` or `src/router.ts`.
- Link with `<RouterLink>`, render the matched page with `<RouterView>`; both are imported from `vue-router`.
- Tests create the router with `createAppRouter(createMemoryHistory())` from `@/router`, `await router.push(...)`, and render with `global: { plugins: [router] }`.
