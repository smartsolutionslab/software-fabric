# Routing: Vue Router

**Status:** Accepted (generated)

## Decision

Vue Router with routes declared in code: `src/router.ts` builds the router from the start page and every module's `route`, which `src/modules/index.ts` finds by itself (`import.meta.glob('./*/routes.ts')`), so adding a module needs no list edited. The app uses the browser's history (`createWebHistory`); tests use `createMemoryHistory` through the same `createAppRouter`. Vue Router has no view transitions of its own, so `createAppRouter` adds them: a `beforeResolve` guard starts `document.startViewTransition` where the browser has it, lets the navigation go on once the browser has captured the page, and ends the transition when the new page has rendered. The first navigation is not one, and a browser without the API navigates as before; with `prefers-reduced-motion: reduce` `src/index.css` sets the transition's animation to none.{{ if project["frontend.ui-library"] == "shadcn-vue" && project["frontend.architecture"] == "spa" }}

With shadcn-vue, `src/App.vue` is the app shell: shadcn-vue's collapsible sidebar holds the start page and each module's page from `moduleRoutes` (no list of modules); breadcrumbs follow the address, from the start page through the module's title to the steps below it; and a command palette opens on Ctrl+K or Cmd+K and goes to any of those pages by keyboard alone. The breadcrumbs and the palette are this pack's (`src/shell/`), since they navigate with Vue Router; their components are the UI library's.{{ end }}

## Consequences

- File-based routing (`unplugin-vue-router`) is refused by lint: one way to declare a route.
- A module's route is one path with one page; nested routes go in its `routes.ts` when a module needs them.
