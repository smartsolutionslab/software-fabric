## Micro-frontend technique: native federation

- A remote exposes exactly `./Page`, the default export of `src/<Module>Page.tsx`, in its `federation.config.js`. A module with several screens routes inside that page, after sharing `{{ if project["frontend.routing"] == "tanstack-router" }}@tanstack/react-router{{ else }}react-router{{ end }}` as below.
- Shared singletons are `react`{{ if project["frontend.design-system"] == "tokens" }} and{{ else }},{{ end }} `react-dom`{{ if project["frontend.design-system"] == "tokens" }}{{ else }} and `@{{ name }}/ui`{{ end }}, with `singleton`, `strictVersion` and `requiredVersion: 'auto'`. To share another library, add it to `shared` in every remote's `federation.config.js`, to `shared` in `web/app/federation.ts` and to the instances in `web/app/src/remotes/federation.ts`, at the same exact version.
- Never import `@softarc/*` or `native-federation-vite` in a screen; the shell's `Remote` component is the only place that loads remotes.
- A remote has no dev server with hot reload: `pnpm dev` rebuilds it on every change, and a reload of the shell shows the change.
- A remote's `pnpm lint` bans the same libraries as the shell's and includes Prettier; run `pnpm format` in the remote to fix what it reports.
