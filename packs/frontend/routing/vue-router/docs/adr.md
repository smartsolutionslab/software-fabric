# Routing: Vue Router

**Status:** Accepted (generated)

## Decision

Vue Router with routes declared in code: `src/router.ts` builds the router from the start page and every module's `route`, which `src/modules/index.ts` finds by itself (`import.meta.glob('./*/routes.ts')`), so adding a module needs no list edited. The app uses the browser's history (`createWebHistory`); tests use `createMemoryHistory` through the same `createAppRouter`.

## Consequences

- File-based routing (`unplugin-vue-router`) is refused by lint: one way to declare a route.
- A module's route is one path with one page; nested routes go in its `routes.ts` when a module needs them.
