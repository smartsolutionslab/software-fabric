## Routing: React Router

- {{ if project["frontend.architecture"] == "micro-frontends" }}A module's route lives in the shell, `src/modules/<module>/`: its `routes.tsx` exports `route`, the app picks it up by itself, and its page mounts the module's remote; the screens themselves live in the remote, `web/modules/<module>`.{{ else }}A module's screens live in `src/modules/<module>/`; its `routes.tsx` exports `route` and the app picks it up by itself.{{ end }} Do not list modules in `App.tsx`.
- Import from `react-router`, never `react-router-dom`.
- Tests render routes inside `MemoryRouter`.
