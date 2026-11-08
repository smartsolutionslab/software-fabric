## Frontend workspace: Turborepo

- Work in `web/`: `pnpm install`, then `pnpm build`, `pnpm lint`, `pnpm test`; each runs `turbo run <task>` over every package, dependencies' builds first. For one package: `pnpm turbo run <task> --filter <package>`, or the script inside the package.
- A package's tasks are its `package.json` scripts. Keep the one root `turbo.json`: no per-package `turbo.json`, no remote cache; `remoteCache.enabled` stays `false`.
- `transit` keys `lint` and `test` on the package's workspace dependencies; keep it, and give a new cached task that reads its dependencies `"dependsOn": ["transit"]`.
- A task sees only the environment variables `turbo.json` names. A build reads `VITE_*` and `REMOTE_*`; a new variable a task needs goes into its `env` there.
- Unchanged tasks are read from the local cache in `web/.turbo/`; never commit it. `pnpm build --force` runs everything again.
- Add a dependency with `pnpm add <name> --filter <package>`; never edit versions by hand without installing.
- Commit `pnpm-lock.yaml`.
