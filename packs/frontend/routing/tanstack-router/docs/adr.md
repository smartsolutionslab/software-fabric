# Routing: TanStack Router

**Status:** Accepted (generated)

## Decision

TanStack Router with routes declared in code. `src/rootRoute.ts` holds the root route, whose page is the app it receives through the router's context. Each module's `src/modules/<module>/routes.tsx` exports `route`, created with `createRoute` below that root route, and adds its type to `ModuleRoutes` (`declare module '@/modules'`). `src/modules/index.ts` finds every module's file by itself (`import.meta.glob('./*/routes.tsx')`), so adding a module needs no list edited. `src/router.tsx` builds the router from the root route, the start page and every module's route, and registers it for TypeScript (`declare module '@tanstack/react-router' { interface Register … }`). `src/App.tsx` is the root route's page: the navigation, then `<Outlet />`. The app uses the browser's history (`createBrowserHistory`); tests use `createMemoryHistory` through the same `createAppRouter`.

## Consequences

- File-based routing (`@tanstack/router-plugin`) is refused by lint, and so are `react-router` and `react-router-dom`: one router, one way to declare a route.
- Links are checked by the compiler: a `<Link to>` (and its `params`) must name a route in the tree, every module's routes included, or `pnpm build` fails. This holds because each module adds its route to `ModuleRoutes` itself; the glob alone yields no path types. Until the first module exists, `ModuleRoute` is any route and no module path is checked.
- A module routes below itself by giving its route children: child routes with `getParentRoute: () => <module route>`, exported as `route = <module route>.addChildren([...])`. The module's page renders `<Outlet />` where they appear. Unlike react-router's `<path>/*`, TanStack Router matches only the routes in its tree, so a page cannot route below itself without them.
- A module's name in the navigation is its route's `staticData.title`.
- The router is created when it first renders (`src/providers/AppRouter.tsx`), not when the providers are composed: its history reads the address once, and a sign-in callback ahead of it may still change it. The routes themselves are created once, at module scope, and every router built from them gets its own context.
