# Frontend workspace: Nx

**Status:** Accepted (generated)

## Decision

`web/` is a pnpm workspace run by Nx. pnpm installs and links the packages (`pnpm-workspace.yaml`, `pnpm-lock.yaml`); Nx runs their tasks. Each frontend application or shared package is a workspace package, and its targets are its own `package.json` scripts: the workspace is package-based, with no Nx plugins and no `project.json` files, so what a package does is what its scripts say. The root's `build`, `lint` and `test` run `nx run-many -t <target>` across all of them, and `build` builds a package's workspace dependencies first (`"dependsOn": ["^build"]` in `nx.json`).

Nx caches `build`, `lint` and `test` locally, in `web/.nx/cache`. A task whose inputs (the package's files, its dependencies' and the lockfile) have not changed is replayed from the cache, with its output and the build's `dist/`, instead of running again. The cache never leaves the machine: `nx.json` sets `"neverConnectToCloud": true`, so Nx neither prompts for nor connects to Nx Cloud.

CI runs the same three on every pull request and keeps no Nx cache between runs, so every package is built, linted and tested from scratch on every run; a cached success can never stand in for a check.

## Consequences

- Commit `web/pnpm-lock.yaml` once `pnpm install` has created it; CI then installs exactly what you tested.
- `web/.gitignore` ignores `.nx/` and the builds' own outputs (`dist/`, `*.tsbuildinfo`). Nx hashes every file of a package that is not ignored, from `web/` down, so a build output it could see would change the next build's inputs and no build would ever be read from the cache.
- A build that writes somewhere other than `dist/` is still cached as a success, but its output is not restored from the cache; add the folder to the package's `nx.outputs` in its `package.json` when another package reads it.
- `pnpm nx reset` clears the local cache when it is in doubt.
- A remote cache (Nx Cloud or self-hosted) and `nx affected` in CI are not used; either would be a decision of its own.
- Dependabot updates the workspace's packages, Nx included, through its npm entry for `/web`, which also updates `pnpm-lock.yaml`.
