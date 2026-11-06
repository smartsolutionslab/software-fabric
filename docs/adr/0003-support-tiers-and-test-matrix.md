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

## Amendment (2026-11-06, M3)

The owner's decisions on the M3 plan. The text above stands; where this
section differs, it takes precedence.

**The matrix domain is every option that has a pack**, not only the
`supported` ones. Read as written, the decision above lets no experimental
option enter the matrix, so none could ever be promoted.

- An option without a pack cannot be generated; it joins the domain in the
  PR that adds its pack.
- A *deferred* option stays in the catalog as experimental and has no pack,
  so it is outside the domain. A *dropped* option leaves the catalog. The
  roadmap's M3 section lists both.
- Each combination has one module set: the module defaults, on every module.
  A defect that needs a second module style, or any three options together,
  is covered by adding that combination explicitly, as above.
- A pairwise matrix over experimental options turns the tiers' meaning
  around: `experimental` now means *not yet passing*, not *not tested*. The
  warning on generation stays.

**An option is promoted to `supported` once its pairs pass**: every matrix
combination that contains it is green in the same nightly run. The PR that
changes its tier cites that run. Adding a pack does not promote its option.
The rule above for a `supported` option that fails stands: a `bug` issue on
the board, no silent demotion.

**Cadence:**

| Layer | Runs | Does |
|---|---|---|
| Pairwise matrix, generation | every PR | generates every combination of the covering set; any generation error fails the PR |
| Pairwise matrix, build and test | nightly, and on demand | builds each combination and runs its unit, architecture and integration tests and its frontend checks; no end-to-end tests |
| Profiles | every PR | unchanged: full CI, end-to-end tests included |

Building the matrix on every PR would take CI capacity the agents need for
their own runs; a nightly run does not compete with them. The definition of
passing above already leaves end-to-end tests out.

**Gate.** The matrix counts as green after two consecutive green nightly
runs, which filters out a single flaky failure. M3's gate is that, with
every option in the domain `supported`.
