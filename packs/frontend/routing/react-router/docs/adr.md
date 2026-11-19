# Routing: React Router

**Status:** Accepted (generated)

## Decision

React Router with a data router (`createBrowserRouter`) around the app, whose one route matches any address, and routes declared in `src/App.tsx` with `<Routes>`/`<Route>`. The data router is there for view transitions: a link's `viewTransition` asks for one, and only a data router runs it (inside `document.startViewTransition`, where the browser has it; a browser without the API navigates as before). Every link of the app asks for one, and with `prefers-reduced-motion: reduce` `src/index.css` sets the transition's animation to none. The router is created when it first renders, since a sign-in callback ahead of it may still change the address. The package is `react-router`; `react-router-dom` is its pre-v7 name and is refused by lint.{{ if project["frontend.ui-library"] == "shadcn" && project["frontend.architecture"] == "spa" }}

With shadcn/ui, `src/App.tsx` is the app shell: shadcn's collapsible sidebar holds the start page and each module's page, found from the `routes.tsx` the modules register (no list of modules); breadcrumbs follow the address, from the start page through the module's title to the steps below it; and a command palette opens on Ctrl+K or Cmd+K and goes to any of those pages by keyboard alone. The breadcrumbs and the palette are this pack's (`src/shell/`), since they navigate with React Router; their components are the UI library's. TanStack Router's shell and the micro-frontend shell keep their navigation bar for now.{{ end }}
