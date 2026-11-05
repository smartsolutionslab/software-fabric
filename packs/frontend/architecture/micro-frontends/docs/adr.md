# Frontend architecture: micro-frontends, a shell and one remote per module

**Status:** Accepted (generated)

## Context

Several teams, or agents, build the screens of different modules at once. In one application every module's screens
are built, tested and released together, so one module's broken build stops every other.

## Decision

The frontend is a **shell** and one **remote** per backend module.

- **The shell** is `web/app`: sign-in, navigation, the providers every screen shares, and the route of each module.
  It holds no module's screens; a module's route mounts that module's remote with `<Remote module="<module>" />`
  (`src/remotes/Remote.tsx`, from the chosen federation technique).
- **A remote** is `web/modules/<module>`, its own workspace package (`@{{ name }}/module-<module>`) with its own build,
  lint and tests. `fabric add-module` creates it with the module; nothing lists the remotes. pnpm finds them through
  `modules/*` in `pnpm-workspace.yaml`, the AppHost through the directories of `web/modules`, the shell through the same
  directories when its configuration is read.
- **Under Aspire** every remote is a Vite dev server of its own, `remote-<module>`. The shell receives each one's
  address as `REMOTE_<MODULE>_URL` (`REMOTE_ORDER_HANDLING_URL` for `order-handling`) and starts once they run
  (`src/AppHost/Wiring/Remotes.cs`).
- **Built**, a remote is served from `/modules/<module>/` next to the shell; that is where the shell looks for it.
- The shell never imports a remote: lint fails on a remote's package name, and the shell's build, dev server and tests
  fail when any import path, relative ones included, loads a remote's file (`web/app/remoteBoundary.ts`). A remote
  reaches other modules only through the backend's API and events, never through another remote's code.
- The shell's Tailwind reads the remotes' sources (`@source '../../modules'`), so a remote's utilities are styled. A
  remote ships no stylesheet of its own: a utility no file used before appears only once the shell is built again, so
  a remote that starts using one is deployed together with a new build of the shell.

## Consequences

- A module's screens build, test and fail on their own; the shell only fails when it cannot load them, and then shows
  that the module is unavailable instead of failing as a whole.
- What every screen needs (sign-in, the design system, React) comes from the shell and is shared; a library whose
  context a remote uses (a router, a query client) must be shared as a singleton by both, or the remote sees none.
- Every remote is one more dev server and one more package to install and update.
