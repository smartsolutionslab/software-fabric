## Routing: Vue Router

- A module's screens live in `src/modules/<module>/`; its `routes.ts` exports `route` and the app picks it up by itself. Do not list modules in `App.vue` or `src/router.ts`.
{{~ if project["frontend.ui-library"] == "shadcn-vue" && project["frontend.architecture"] == "spa" ~}}
- `src/App.vue` is the app shell; it reads `moduleRoutes` for the sidebar, and so do the breadcrumbs (`src/shell/Breadcrumbs.vue`) and the command palette (`src/shell/CommandPalette.vue`). A route's `title` is its name in all three. Its tests are `src/shell/AppShell.test.ts`.
{{~ end ~}}
- Link with `<RouterLink>`, render the matched page with `<RouterView>`; both are imported from `vue-router`.
- Every navigation after the first is a view transition where the browser has the View Transitions API: `src/router.ts` resolves it inside `document.startViewTransition`, and the page changes as before where the browser has not. With reduced motion `src/index.css` takes the transition's animation away, so the page changes at once. No generated module has a detail page; to carry an element from a list row to its detail page, give the row's element and the detail page's heading the same CSS `view-transition-name`, unique on each page, never on more than one element at a time.
- Tests create the router with `createAppRouter(createMemoryHistory())` from `@/router`, `await router.push(...)`, and render with `global: { plugins: [router] }`.
