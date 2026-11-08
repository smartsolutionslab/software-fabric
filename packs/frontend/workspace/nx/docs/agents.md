## Frontend workspace: Nx

- Work in `web/`: `pnpm install`, then `pnpm build`, `pnpm lint`, `pnpm test`; each runs `nx run-many -t <target>` over every package, dependencies' builds first. For one package: `pnpm nx run <package>:<target>`, or the script inside the package.
- A package's targets are its `package.json` scripts. Do not add Nx plugins, `project.json` files or Nx Cloud; `neverConnectToCloud` stays `true`.
- Unchanged targets are read from the local cache in `web/.nx/`; never commit it. `pnpm nx reset` clears it.
- Add a dependency with `pnpm add <name> --filter <package>`; never edit versions by hand without installing.
- Commit `pnpm-lock.yaml`.
