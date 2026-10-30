# Frontend workspace: pnpm

**Status:** Accepted (generated)

## Decision

`web/` is a pnpm workspace. Each frontend application or shared package is a workspace package; the root runs `build`, `lint` and `test` across all of them. CI runs the same three on every pull request.

## Consequences

- Commit `web/pnpm-lock.yaml` once `pnpm install` has created it; CI then installs exactly what you tested.
