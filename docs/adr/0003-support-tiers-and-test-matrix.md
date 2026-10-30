# ADR-0003: Support tiers and the test matrix

**Status:** Proposed
**Date:** 2026-10-30

## Context

The catalog allows far more combinations than any CI can build. Testing
each option in isolation misses the defects that matter most here — the
ones that appear when two options meet (a UI library and a styling
approach, an event store and a database, a bus and a tenancy model).

Every option is also permanent maintenance: each library upgrade touches
every pack that uses the library.

## Decision

**Three layers of testing:**

| Layer | Runs | Generates and builds |
|---|---|---|
| Generator unit tests | every PR | — (rule evaluation, slot rendering, determinism) |
| Profiles | every PR | every profile in the catalog, full build + tests + gates |
| Pairwise matrix | nightly | a covering set in which every pair of supported options from different dimensions appears at least once |

A generated project counts as passing when it restores, builds with
warnings as errors, runs its unit, architecture and integration tests,
and its frontend builds, lints and passes its tests.

**Two tiers per option:**

- `supported` — appears in the profile run or the pairwise matrix and
  passed its last run.
- `experimental` — generated with a warning; not in the matrix.

An option is promoted to `supported` only by being added to the matrix
and passing. A `supported` option whose nightly run fails becomes a
`bug` issue on the board; it is not silently demoted.

## Consequences

- Pairwise does not prove every combination works. A defect needing
  three specific options together can slip through; when one is found,
  that triple is added to the matrix explicitly.
- Nightly cost grows with the catalog. Budget is a project decision;
  the matrix runner reports duration per combination so it can be watched.
- Dependency upgrades (Renovate/Dependabot) run through the same layers
  and are well-suited agent issues.
