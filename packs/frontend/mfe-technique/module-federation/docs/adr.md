# Micro-frontend technique: module federation

**Status:** Accepted (generated)

## Context

The shell has to load each module's remote at run time, share React and the design system with it as one instance,
and work in development without building every remote first.

## Decision

**Module federation 2.0 through `@module-federation/vite`** (1.23.x), on the shell and on every remote.

- **A remote** (`web/modules/<module>/vite.config.ts`) is named after its module and exposes one entry, `./Page`: the
  module's screens as one component, the default export of `src/<Module>Page.tsx`. Its `remoteEntry.js` is what the
  shell loads.
- **The shell** (`web/app/federation.ts`) declares every directory of `web/modules` as a remote, at the address the
  AppHost gives it, `/modules/<module>/` when built. `src/remotes/Remote.tsx` loads a module's `./Page` with
  `loadRemote` from `@module-federation/runtime` once, shows it, and shows that the module is unavailable when its
  remote cannot be loaded.
- **Shared as singletons** by both sides: `react`, `react-dom` and `@{{ name }}/ui`. A remote gets them from the shell.
- No type declarations are exchanged (`dts: false`): a remote is loaded by name, and its page takes no props.
- Each remote's `lint` runs ESLint and Prettier, its `build` type-checks it and builds `dist/remoteEntry.js`, and its
  tests run with Vitest.

## Consequences

- Remotes run in development as plain Vite dev servers; nothing is built before the shell can load them.
- A library both sides use with shared state (a router, a query client) must be added to `shared` in both
  `federation.ts` and the remote's `vite.config.ts`, with the same version.
- The shell's end-to-end tests open each module and check that its page came from the remote's own server.
