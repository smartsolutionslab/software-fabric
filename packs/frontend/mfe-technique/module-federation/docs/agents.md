## Micro-frontend technique: module federation

- A remote exposes exactly `./Page`, the default export of `src/<Module>Page.tsx`. A module with several screens routes inside that page, after sharing `react-router` as below.
- Shared singletons are `react`, `react-dom` and `@{{ name }}/ui`. To share another library, add it to `shared` in `web/app/federation.ts` and in every remote's `vite.config.ts`, at the same version.
- Never import `@module-federation/*` in a screen; the shell's `Remote` component is the only place that loads remotes.
- A remote's `pnpm lint` bans the same libraries as the shell's and includes Prettier; run `pnpm format` in the remote to fix what it reports.
