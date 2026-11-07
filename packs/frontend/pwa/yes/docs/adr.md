# Progressive web app: yes

**Status:** Accepted (generated)

## Decision

The app is installable and its shell loads offline. `vite-plugin-pwa` (Workbox's `generateSW`) builds the service worker and the web app manifest from `vite.config.ts`; a script it writes into `index.html` registers the worker, so nothing in `src` registers one. `public/icon.svg` is the manifest's icon.

The worker keeps the app shell and nothing else:

- The build's files (scripts, styles, HTML, SVG, fonts) are precached; a navigation is answered with `index.html`, except under `/api/`.
- At run time it keeps same-origin files the page loads as script, style, font, image or manifest, network first. In development these are the dev server's modules, which is how the end-to-end tests see the shell offline.
- `/api` is never cached: those calls are fetches, which no route matches, so the browser sends them to the network as if there were no worker. Tenant data and anything sent with a token stay out of the cache.
- The identity provider is another origin, which no route matches: its pages, keys and tokens are never cached.

A new worker takes over at once (`skipWaiting`, `clientsClaim`); an open tab gets the new shell on its next load.

The worker runs under `pnpm dev` as well, so what CI tests is what a developer runs; its development files go to `node_modules/.vite-pwa`.

## Consequences

- Offline, the app shows its shell and the signed-in user from the browser's session; every `/api` call fails as it would without a worker, and screens show their error states.
- A sign-in needs the identity provider and so the network; a user signed out offline sees the sign-in fail.
- A page loaded before a deployment that then lazily loads a chunk the deployment removed fails that load; reloading fixes it. There is no "update available" prompt.
- `e2e/offline.spec.ts` signs in, lets the worker take the page, reads `/api/me` online, goes offline and reloads: the app must still render and `/api/me` must be unreachable. A worker route that caches `/api` answers fails it.
