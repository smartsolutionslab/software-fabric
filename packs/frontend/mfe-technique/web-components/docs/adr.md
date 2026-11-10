{{~ vue = project["frontend.framework"] == "vue" ~}}
{{~ ds = project["frontend.design-system"] ~}}
{{~ framework = "React" ~}}
{{~ if vue ~}}
{{~ framework = "Vue" ~}}
{{~ end ~}}
# Micro-frontend technique: web components

**Status:** Accepted (generated)

## Context

The shell has to load each module's remote at run time and show its screens, and work in development without a step a
developer runs by hand. A custom element is the browser's own boundary between a page and what renders inside it: the
shell needs to know only a tag, not the framework behind it or the version of that framework.

## Decision

**Each remote defines its page as a custom element with its framework's own wrapper**, and the shell loads it as a plain
script module and mounts its tag.

- **A remote** (`web/modules/<module>`) defines one element, `<remote-<module>>`, in `src/element.ts`: the module's
  screens as one component, the default export of `src/<Module>Page.{{ if vue }}vue{{ else }}tsx{{ end }}`, wrapped by
{{~ if vue ~}}
  Vue's own `defineCustomElement`.
{{~ else ~}}
  **`@r2wc/react-to-web-component` 2.1.1**, pinned exactly: React has no wrapper of its own, and r2wc renders the
  component into the element with the remote's React when the element is added to the page and unmounts it when it is
  removed.
{{~ end ~}}
  The element has no shadow root{{ if vue }} (`shadowRoot: false`){{ end }}, so the shell's stylesheet styles the page as it styles the shell's
  own screens.
- **`pnpm build`** type-checks the remote and builds one script, `dist/remote.js`, with Vite: the page, its packages and
  its own copy of {{ framework }}. Nothing is shared with the shell, so a remote's version of {{ framework }} is its own
  to choose and to upgrade, and the shell never has to match it.
- **The remote's own stylesheet**, from a CSS file or module it imports, stays where Vite writes it in `dist`, and
  `remote.js` links it relative to itself (`styles.ts`): the shell loads only that one script, and a stylesheet linked
  from the remote's address keeps its `url()`s pointing at the remote's assets, not at the shell's.
- **Under Aspire** a remote stays a Vite app: `pnpm dev` is `vite preview`, whose `serve.ts` plugin first builds the
  remote in watch mode and then serves `dist`. A change is rebuilt in the background and shows after a reload of the
  shell, without hot module replacement; the shell always loads a built `remote.js`, as it does once deployed.
- **The shell** (`web/app/remotes.ts`) finds every directory of `web/modules` and gives each remote's `remote.js` the
  address the AppHost gives it, `/modules/<module>/remote.js` when built. `src/remotes/elements.ts` imports a module's
  script once and checks that it defined `<remote-<module>>`; `src/remotes/Remote.{{ if vue }}vue{{ else }}tsx{{ end }}`
  then mounts the tag, and shows that the module is unavailable when its remote cannot be loaded. The next time that
  page is opened it imports the script under a new address (`?retry=<n>`): the browser keeps a failed module import for
  good, so only a new address loads it once its server is back, without reloading the shell. A remote is one file, so
  the retry loads all of it.
- A remote's page renders with its own copy of {{ framework }} into its own root, so a page that fails to render takes
  only its element down, never the shell.
{{~ if ds == "tokens" ~}}
- **No ui package**: the tokens design system has no components, so a remote styles its screens with the tokens' CSS
  variables, which the shell's stylesheet defines.
{{~ else if ds == "tokens-and-components" ~}}
- **The ui package is bundled into each remote**: its components are {{ framework }} components, rendered with the
  remote's own {{ framework }}, and styled by the shell's stylesheet like the remote's other screens.
{{~ else if ds == "web-components" ~}}
- **The design system's elements are defined once, by the shell**: a page's custom elements share one registry, and a
  second `customElements.define` of a tag throws. A remote's page renders their tags (`<ds-badge>`) without importing
  `@{{ name }}/ui`, which its lint refuses except for types{{ if vue }}; Vue's compiler leaves them to the browser
  through the remote's `isCustomElement` in `vite.config.ts`, as in the shell's{{ else }}, and its `src/design-system.d.ts`
  declares them for React's JSX as the shell's does{{ end }}. The remote's tests define them in `src/test-setup.ts`, as the
  shell's tests do.
{{~ end ~}}
- No type declarations are exchanged: a remote is loaded by its module's name, and its element takes no attributes.
- Each remote's `lint` runs ESLint, with the shell's library bans and test rules, and Prettier, and its tests run with
  Vitest.

## Consequences

- Every remote ships its own {{ framework }}: the browser downloads it once per remote, and a library with shared state
  (a router, a query client) is a separate instance in each remote, which sees none of the shell's. A remote that needs
  the shell's state receives it through the backend's API and events, never through a shared instance.
- Remotes are built, not served from source, even in development: the first start of a remote takes a build, and a
  change shows after its rebuild and a reload.
- A remote's `remote.js` has a fixed name, so a deployment that caches it serves it with revalidation (an `ETag` and
  `Cache-Control: no-cache`); otherwise a browser may keep an old remote after a new one is deployed.
- The shell's end-to-end tests open each module, check that its page is the element its remote defines, loaded from the
  remote's own server, and that it shows again after the remote was down and when it is opened a second time.
