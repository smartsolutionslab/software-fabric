## Micro-frontend technique: web components

- A remote defines exactly one element, `<remote-<module>>`, in `src/element.ts`, from the default export of `src/<Module>Page.{{ if project["frontend.framework"] == "vue" }}vue{{ else }}tsx{{ end }}`{{ if project["frontend.framework"] == "vue" }} with Vue's `defineCustomElement`{{ else }} with `r2wc` from `@r2wc/react-to-web-component`{{ end }}, without a shadow root. A module with several screens routes inside that page with its own router.
- A remote shares nothing with the shell: it bundles its own {{ if project["frontend.framework"] == "vue" }}Vue{{ else }}React{{ end }} and libraries into `dist/remote.js`. Pass data between the shell and a remote through the backend's API and events, never through a shared instance or `window`.
{{~ if project["frontend.design-system"] == "web-components" ~}}
- Render the design system's elements (`<ds-badge>`) in a remote without importing `@{{ name }}/ui`; the shell defines them once for the page. Import only its types; lint refuses the rest.
{{~ end ~}}
- Never import a remote's script in a screen; the shell's `Remote` component and `src/remotes/elements.ts` are the only places that load remotes.
- A remote has no dev server with hot reload: `pnpm dev` rebuilds it on every change, and a reload of the shell shows the change.
- A remote's `pnpm lint` bans the same libraries as the shell's and includes Prettier; run `pnpm format` in the remote to fix what it reports.
