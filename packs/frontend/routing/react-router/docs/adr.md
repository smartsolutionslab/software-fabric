# Routing: React Router

**Status:** Accepted (generated)

## Decision

React Router in declarative mode: `BrowserRouter` around the app, routes declared in `src/App.tsx` with `<Routes>`/`<Route>`. The package is `react-router`; `react-router-dom` is its pre-v7 name and is refused by lint.{{ if project["frontend.ui-library"] == "shadcn" && project["frontend.architecture"] == "spa" }}

With shadcn/ui, `src/App.tsx` is the app shell: shadcn's collapsible sidebar holds the start page and each module's page, found from the `routes.tsx` the modules register (no list of modules); breadcrumbs follow the address, from the start page through the module's title to the steps below it; and a command palette opens on Ctrl+K or Cmd+K and goes to any of those pages by keyboard alone. The breadcrumbs and the palette are this pack's (`src/shell/`), since they navigate with React Router; their components are the UI library's. TanStack Router's shell and the micro-frontend shell keep their navigation bar for now.{{ end }}
