# ADR-0002: Option packs and slots

**Status:** Proposed
**Date:** 2026-10-30

## Context

V1 contains every option in the catalog — roughly 60–80 options across
backend, tests and frontend. Implementing them with conditionals inside
shared templates (`dotnet new` symbols, `#if` blocks) makes every option
a change to files every other option also changes. That does not scale to
this many options, and it rules out building them in parallel.

## Decision

**One option, one pack.** A pack lives in `packs/<dimension>/<option>/`
and is described by `pack.yaml` (schema: `schema/pack.schema.json`).

A pack contributes to the generated project in exactly two ways:

1. **Files it owns.** Paths no other pack writes to. The generator fails
   if two selected packs claim the same target path.
2. **Slot contributions.** A slot is a named extension point declared by
   the base or by another pack — e.g. `backend.module.registration`,
   `apphost.resources`, `frontend.app.bootstrap`. A slot has a cardinality
   (`zero-or-more`, `exactly-one`, `at-most-one`); contributions are
   ordered by `order`, then by option id, so output is deterministic.

A pack never edits another pack's output. When a pack needs to influence
something it does not own, the fix is a new slot, introduced in its own
change.

**Every pack ships four things besides code**, and the schema rejects a
pack without them:

| Field | Purpose |
|---|---|
| `adr` | The decision record the generated project receives |
| `agents.agents-md` | The rules agents follow in that project |
| `agents.briefs` | Role-specific additions (engineer, reviewer, …) |
| `gates` | At least one check that fails the build when the rule is broken |

Templates are rendered with Scriban. The render model contains the
resolved choices (`fabric.yaml`), the module list, and nothing derived
from the machine or the clock.

## Consequences

- Packs are independent units of work. Once the slot set is stable, packs
  can be built in parallel — which is how M3 is meant to be delivered.
- The quality of the slot set decides the cost of every pack. That is why
  M0/M1 (base, slots, first full profile) are supervised.
- Some code will be duplicated across sibling packs (e.g. PrimeVue and
  Vuetify setting up similar layouts). Accepted: sharing across packs
  would couple them again.
- "An option without a gate is not done" is a schema rule, not a review
  convention.
