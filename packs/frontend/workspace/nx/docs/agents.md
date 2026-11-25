## Frontend workspace: Nx

- Work in `web/`: `pnpm install`, then `pnpm build`, `pnpm lint`, `pnpm test`; each runs `nx run-many -t <target>` over every package, dependencies' builds first. For one package: `pnpm nx run <package>:<target>`, or the script inside the package.
- A package's targets are its `package.json` scripts. Do not add Nx plugins, `project.json` files or Nx Cloud; `neverConnectToCloud` stays `true`.
- Unchanged targets are read from the local cache in `web/.nx/`; never commit it. `pnpm nx reset` clears it.
- Add a dependency with `pnpm add <name> --filter <package>`; never edit versions by hand without installing.
- Commit `pnpm-lock.yaml`.
- `web/pnpm-workspace.yaml` sets `minimumReleaseAge: 1440`, so pnpm installs no release less than a day old. Never remove or lower it. To take a security fix sooner, add only that version to `minimumReleaseAgeExclude` (`- "<package>@<version>"`, quoted) with the reason in the pull request (the advisory), and remove the entry once the version is a day old. Pin exact versions only once they are a day old.
