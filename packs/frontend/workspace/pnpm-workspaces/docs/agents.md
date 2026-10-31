## Frontend workspace: pnpm

- Work in `web/`: `pnpm install`, then `pnpm build`, `pnpm lint`, `pnpm test` (all packages) or the same inside one package.
- Add a dependency with `pnpm add <name> --filter <package>`; never edit versions by hand without installing.
- Commit `pnpm-lock.yaml`.
