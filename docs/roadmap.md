# Roadmap

V1 contains every option in `catalog/catalog.yaml`. The milestones order
the work; they do not cut scope.

| Milestone | Content | Who builds | Gate |
|---|---|---|---|
| **M0 Core** | CLI, catalog loading and rule evaluation, pack loading and schema validation, slot rendering, `fabric.yaml`, determinism tests, profile CI | Supervised | Human review of the slot set and rule evaluator |
| **M1 Vertical slice** | Profile `saas-standard-react` end to end, including the fabric half (AGENTS.md assembly, agents, skills, board workflows, cross-model review) | Supervised | A generated project builds, passes, and one agent delivers one issue in it |
| **M2 Second option per dimension** | Vue, SQL Server, CRUD, event sourcing, micro-frontends, Pinia, … | 3–5 agents | Profiles green; no pack had to edit another |
| **M3 Breadth** | Every remaining pack | 10–20 agents | Pairwise matrix green |
| **M4 Profiles and pilot** | Final profiles, docs, first real product generated | Mixed | Pilot product running on the fabric |

## M0 tasks

Each is one issue, one PR. Tests first.

| # | Task | Done when |
|---|---|---|
| M0-01 | Solution skeleton: `src/Fabric.Cli`, `src/Fabric.Core`, `tests/Fabric.Core.Tests`, `Directory.Build.props` (nullable, warnings as errors), CI building it | CI green on an empty test |
| M0-02 | Catalog model + YAML loader | Loads `catalog/catalog.yaml`; unknown fields are an error |
| M0-03 | Condition parser for the grammar in `docs/catalog-rules.md` | Every condition string in the catalog parses; malformed ones fail with position |
| M0-04 | Choice resolution: profile `extends`, defaults, `active-when` | Each profile resolves to a full choice set; setting an inactive dimension is rejected |
| M0-05 | Rule evaluation: `requires`, `conflicts`, per-module semantics | Every profile is valid; a table of known-invalid combinations is rejected with one-sentence reasons |
| M0-06 | Catalog self-check | CI fails if a condition names a dimension/option that does not exist, or a profile is invalid |
| M0-07 | Pack loader + schema validation | A pack without a gate or ADR is rejected |
| M0-08 | Slot engine: declarations, cardinality, ordering, file-ownership collisions | Golden-file tests for ordering; collision and cardinality errors tested |
| M0-09 | Scriban rendering with the restricted model | Same input twice → byte-identical output |
| M0-10 | `fabric new --profile <p> [--set dim=opt]` and `fabric validate` | Writes `fabric.yaml` + base project for a profile using stub packs |
| M0-11 | `fabric add-module <name> --set module.style=…` | Adds a module to an existing generated project |
| M0-12 | Profile CI: generate every profile and build it | Job exists and runs against stub packs |

**Gate after M0:** review of the slot list and the rule evaluator before
any real pack is written.
