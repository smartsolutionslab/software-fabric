## Routing: Vue Router

- A module's screens live in `src/modules/<module>/`; its `routes.ts` exports `route` and the app picks it up by itself. Do not list modules in `App.vue` or `src/router.ts`.
{{~ if project["frontend.ui-library"] == "shadcn-vue" && project["frontend.architecture"] == "spa" ~}}
- `src/App.vue` is the app shell; it reads `moduleRoutes` for the sidebar, and so do the breadcrumbs (`src/shell/Breadcrumbs.vue`) and the command palette (`src/shell/CommandPalette.vue`). A route's `title` is its name in all three. Its tests are `src/shell/AppShell.test.ts`.
{{~ end ~}}
- Link with `<RouterLink>`, render the matched page with `<RouterView>`; both are imported from `vue-router`.
- Tests create the router with `createAppRouter(createMemoryHistory())` from `@/router`, `await router.push(...)`, and render with `global: { plugins: [router] }`.
