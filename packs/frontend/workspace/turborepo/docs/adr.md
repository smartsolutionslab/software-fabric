# Frontend workspace: Turborepo

**Status:** Accepted (generated)

## Decision

`web/` is a pnpm workspace run by Turborepo. pnpm installs and links the packages (`pnpm-workspace.yaml`, `pnpm-lock.yaml`); Turborepo runs their tasks. Each frontend application or shared package is a workspace package, and its tasks are its own `package.json` scripts: the workspace is package-based, with a single root `turbo.json` and no per-package `turbo.json`, so what a package does is what its scripts say. The root's `build`, `lint` and `test` run `turbo run <task>` across all of them, and `build` builds a package's workspace dependencies first (`"dependsOn": ["^build"]` in `turbo.json`).

Turborepo caches `build`, `lint` and `test` locally, in `web/.turbo/cache`. A task whose inputs (the package's files, its dependencies' and the lockfile) have not changed is replayed from the cache, with its output and the build's `dist/` and `storybook-static/`, instead of running again. A build is also keyed on every `VITE_*` and `REMOTE_*` environment variable (`"env": ["VITE_*", "REMOTE_*"]`), the values Vite and module federation bake into a bundle, so a build against another sign-in authority or remote URL runs again instead of replaying a bundle that points elsewhere. Turborepo hashes a workspace dependency into a task only along `dependsOn`, so `lint` and `test` depend on `transit`, a task no package has a script for, which depends on `^transit`: a change in a package's workspace dependencies, such as the tokens, then runs its own lint and test again instead of replaying them. The cache never leaves the machine: `turbo.json` sets `"remoteCache": { "enabled": false }`, so Turborepo neither reads from nor writes to a remote cache, even with `TURBO_TOKEN` set or after `turbo login`.

CI runs the same three on every pull request and keeps no Turborepo cache between runs, so every package is built, linted and tested from scratch on every run; a cached success can never stand in for a check.

## Telemetry

Turborepo's default is kept: `turbo` sends anonymous usage telemetry unless it is turned off, and the generated project neither turns it off nor changes CI for it. To opt out, run `turbo telemetry disable` once on a machine, or set `TURBO_TELEMETRY_DISABLED=1` or `DO_NOT_TRACK=1` in the environment, for example in CI.

## Consequences

- Commit `web/pnpm-lock.yaml` once `pnpm install` has created it; CI then installs exactly what you tested.
- `web/.gitignore` ignores `.turbo/` and the builds' own outputs (`dist/`, `*.tsbuildinfo`). Turborepo hashes every file of a package that is not ignored, so a build output it could see would change the next build's inputs and no build would ever be read from the cache.
- Turborepo runs tasks in strict environment mode: a task sees the system's basic variables and those `turbo.json` names, so a build sees every `VITE_*` and `REMOTE_*` variable and nothing else of yours. A variable a build or test needs is added to that task's `env` in `turbo.json`, which also makes it part of the task's key.
- A build that writes somewhere other than `dist/` or `storybook-static/` is still cached as a success, but its output is not restored from the cache; add the folder to `build.outputs` in `turbo.json` when another package reads it.
- Delete `web/.turbo/cache`, or run with `--force`, when the local cache is in doubt.
- A remote cache (Vercel or self-hosted) and `--affected` in CI are not used; either would be a decision of its own.
- Dependabot updates the workspace's packages, Turborepo included, through its npm entry for `/web`, which also updates `pnpm-lock.yaml`.
