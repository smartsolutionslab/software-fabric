# Frontend workspace: pnpm

**Status:** Accepted (generated)

## Decision

`web/` is a pnpm workspace. Each frontend application or shared package is a workspace package; the root runs `build`, `lint` and `test` across all of them. CI runs the same three on every pull request.

`web/pnpm-workspace.yaml` sets `minimumReleaseAge: 1440`: pnpm installs no version published less than a day ago. A just-published release is often not yet served by the registry, nor are its new dependencies (`ERR_PNPM_NO_MATCHING_VERSION`, `ERR_PNPM_FETCH_404`), and a day lets a bad or compromised release be withdrawn before it reaches the workspace. CI fails when the setting is missing.

## Consequences

- Commit `web/pnpm-lock.yaml` once `pnpm install` has created it; CI then installs exactly what you tested.
- Dependabot updates the workspace's packages through its npm entry for `/web`, which also updates `pnpm-lock.yaml`.
- A committed lockfile still installs as is. `pnpm add`, or a pin of a release less than a day old, fails with `ERR_PNPM_NO_MATURE_MATCHING_VERSION` until the release is a day old. A security fix that cannot wait is taken by listing only that version under `minimumReleaseAgeExclude`, with the reason in the pull request; the setting itself is never removed or lowered.
