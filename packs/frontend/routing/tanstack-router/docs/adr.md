# Routing: TanStack Router

**Status:** Accepted (generated)

## Decision

TanStack Router with routes declared in code: `src/router.tsx` builds the router from the app as its root route, the start page, and every module's `route`, which `src/modules/index.ts` finds by itself (`import.meta.glob('./*/routes.tsx')`), so adding a module needs no list edited. `src/App.tsx` is the root route's page: the navigation, then `<Outlet />`. The app uses the browser's history (`createBrowserHistory`); tests use `createMemoryHistory` through the same `createAppRouter`.

## Consequences

- File-based routing (`@tanstack/router-plugin`) is refused by lint, and so are `react-router` and `react-router-dom`: one router, one way to declare a route.
- The router is not registered for TypeScript (`declare module '@tanstack/react-router'`): module routes are found at build time, so their paths are strings, and a link to one is not checked against the route tree.
- A module's route is one path with one page; nested routes go in its `routes.tsx` when a module needs them.
