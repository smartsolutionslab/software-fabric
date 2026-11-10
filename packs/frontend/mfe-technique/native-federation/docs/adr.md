{{~ vue = project["frontend.framework"] == "vue" ~}}
{{~ framework = "React" ~}}
{{~ if vue ~}}
{{~ framework = "Vue" ~}}
{{~ end ~}}
{{~ if vue && project["frontend.design-system"] == "tokens" ~}}
{{~ shared = "`vue`" ~}}
{{~ else if vue ~}}
{{~ shared = "`vue` and `@" + name + "/ui`" ~}}
{{~ else if project["frontend.design-system"] == "tokens" ~}}
{{~ shared = "`react`, its JSX runtimes and `react-dom`" ~}}
{{~ else ~}}
{{~ shared = "`react`, its JSX runtimes, `react-dom` and `@" + name + "/ui`" ~}}
{{~ end ~}}
# Micro-frontend technique: native federation

**Status:** Accepted (generated)

## Context

The shell has to load each module's remote at run time, share {{ framework }} and the design system with it as one instance,
and work in development without a step a developer runs by hand. Native federation does this with plain ES modules
and an import map, without a bundler's runtime: a remote's build writes `remoteEntry.json`, which names the modules it
exposes and the packages it shares.

## Decision

**Native federation v4**: each remote builds with **`native-federation-vite` 0.2.0** on `@softarc/native-federation`
4.8.1, and the shell loads remotes with **`@softarc/native-federation-orchestrator` 4.9.0**. All three are pinned
exactly.

- **The adapter is young.** Native federation's maintainers ship adapters for Angular and esbuild, not for Vite.
  `native-federation-vite` is a third-party package with one maintainer; 0.2.0 was about two weeks old when it was
  chosen, its README describes it as early with an API that may change before 1.0, and it was written for Vite remotes
  in an Angular shell. It peers on core `~4.8.1`, so core is pinned there. Upgrade the three together, and run the
  micro-frontend end-to-end tests on every upgrade.
- **A remote** (`web/modules/<module>/federation.config.js`) is named after its module and exposes one entry, `./Page`:
  the module's screens as one component, the default export of `src/<Module>Page.{{ if vue }}vue{{ else }}tsx{{ end }}`. `pnpm build` type-checks it,
  bundles the page once with Vite without writing it, so the remote's boundary names a file of another remote or of
  the shell (native federation's dependency analysis would stop at it saying only that it is outside the remote), and
  runs `native-federation-vite build`, which reuses the plugins of the remote's `vite.config.ts` and writes
  `dist/remoteEntry.json` with content-hashed bundles.
- **Under Aspire** a remote stays a Vite app: `pnpm dev` is `vite preview`, whose `serve.ts` plugin first builds the
  remote with native federation in watch mode, unminified, and then serves `dist`. Native federation has no dev server;
  a change is rebuilt in the background and shows after a reload of the shell, without hot module replacement.
- **The shell is the host.** `src/remotes/federation.ts` starts the orchestrator once, with the shell's own
  `remoteEntry.json`: {{ shared }} at the shell's installed versions (`web/app/federation.ts`
  reads them). The import map sends each of them to a module that exports the shell's own instance, so a remote renders
  with the shell's {{ framework }} and {{ if vue }}its reactivity works across the shell and the remote{{ else }}its hooks work{{ end }}. The shell itself is built and served by Vite as before; nothing about it
  is federated except what it shares.
- **Shared as singletons with strict versions** by both sides: {{ shared }}, each with
  `singleton: true, strictVersion: true, requiredVersion: 'auto'`, so a remote's required version is the exact pin in
  its package.json. The orchestrator runs with `strictExternalCompatibility`: a remote built against another version
  fails to load and the shell says so, instead of loading a second {{ framework }} whose {{ if vue }}components and reactivity{{ else }}hooks{{ end }} would fail at run time.
{{- if project["frontend.design-system"] != "tokens" }} The ui
  package is a workspace package; native federation shares a package only when its package.json names a version, so
  this pack gives it `"version": "0.0.0"`, and its required version is `workspace:*`.{{ end }}
{{~ if vue ~}}
- **Every shared package is kept** (`features: { ignoreUnusedDeps: false }`): native federation drops a shared package no
  source file imports, and a page's compiled template imports `vue` where its source does not, so `vue` would be
  bundled into the page as a second copy. `vue` is shared with `includeSecondaries: false`: its compiler and server
  renderer are no page's imports.
{{~ else ~}}
- **Every shared package is kept** (`features: { ignoreUnusedDeps: false }`): native federation drops a shared package no
  source file imports, and a page's compiled JSX imports `react/jsx-runtime` (`react/jsx-dev-runtime` under Aspire,
  where `NODE_ENV` is `development`) where its source imports nothing from React, so React's JSX runtime would be
  bundled into the page. The packages are shared with `includeSecondaries: false`, and the two JSX runtimes on their
  own: `react-dom/server` and React's compiler runtime are no page's imports.
{{~ end ~}}
- `src/remotes/Remote.{{ if vue }}vue{{ else }}tsx{{ end }}` loads a module's `./Page` once, shows it, and shows that the module is unavailable when its
  remote cannot be loaded. The next time that page is opened it reads the remote's `remoteEntry.json` again, and imports
  a module that failed under a new address (`?retry=<n>`): the browser keeps a failed module import for good, so only a
  new address loads it once its server is back, without reloading the shell.
- A module the page imports that failed, other than the page's own, loads again only after a reload of the shell;
  recovering it in place would need each retry served under its own path.
- The orchestrator registers a remote once per document: once its `remoteEntry.json` has been read, it is not read
  again (orchestrator 4.9.0 answers a second `initRemoteEntry` for a known remote with a warning). A remote redeployed
  while the shell is open, with new content-hashed file names, is therefore picked up after a reload of the shell; until
  then a retry asks for the file names the shell first read.
{{~ if project["frontend.design-system"] == "tokens" ~}}
- **No ui package is shared**: the tokens design system has no components, so a remote shares only {{ framework }} with the
  shell, and styles its screens with the tokens' CSS variables, which the shell's stylesheet defines.
{{~ else if project["frontend.design-system"] == "web-components" ~}}
- **The design system's elements are defined once**, by the shell's instance of `@{{ name }}/ui`: a remote's page imports
  the package for its elements and renders them as tags (`<ds-badge>`), which {{ if vue }}Vue's compiler leaves to the
  browser as custom elements through the remote's `isCustomElement` in `vite.config.ts`, as in the shell's{{ else }}its `src/design-system.d.ts` declares
  for React's JSX as the shell's does{{ end }}.
{{~ end ~}}
- No type declarations are exchanged: a remote is loaded by name, and its page takes no props.
- Each remote's `lint` runs ESLint, with the shell's library bans and test rules, and Prettier, and its tests run with
  Vitest.

## Consequences

- Remotes are built, not served from source, even in development: the first start of a remote takes a build, and a
  change shows after its rebuild and a reload.
- A library both sides use with shared state (a router, a query client) must be added to the shared lists in the
  remotes and the shell, with the same exact version; otherwise each remote uses its own copy and sees none of the
  shell's state.
- The shell's end-to-end tests open each module, check that its page came from the remote's own server, and that the
  remote's own copies of the shared packages were never loaded.
