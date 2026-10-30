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
| M0-12 | Profile CI: generate every profile and build it | Job exists and runs against stub packs. *As built:* every fixture profile is generated twice and compared, a module is added, every real profile is validated. Building generated projects needs real packs and moves to M1. |

**Gate after M0:** review of the slot list and the rule evaluator before
any real pack is written.

## M1 tasks

Profile `saas-standard-react`: 37 packs plus the base. Each task is one
branch; tests first. Docker is available, so generated projects run their
Aspire integration tests here and in CI.

| # | Task | Done when |
|---|---|---|
| M1-01 | Slot set (`docs/slots.md`), ADR-0005, and the generator changes it needs: slot separators, generator-written ADRs / AGENTS.md sections / agent briefs, project name | Slot set approved at the gate; generator changes tested against the fixtures |
| M1-02 | Base pack: repo skeleton, Aspire AppHost and ServiceDefaults, BuildingBlocks, Contracts, per-module csproj skeletons, `build.proj`, AGENTS.md, CLAUDE.md, CI. A test helper generates a real profile with empty stand-ins for packs not written yet | `saas-standard-react` generates with stand-ins and `dotnet build build.proj` passes |
| M1-03 | Backend core: modular-monolith, minimal-apis, rest, result, hand-rolled mediator, postgres, ef-core, keycloak | Host starts under Aspire; an endpoint answers |
| M1-04 | Modules: ddd (light), cqrs logical. *As built:* also xunit, shouldly, nsubstitute (moved from M1-06 — a module's tests need a framework); integration-events moved to M1-05, where the bus that carries them is built | `fabric add-module billing` in a generated project builds and its tests pass |
| M1-05 | Messaging and tenancy: integration-events, rabbitmq, wolverine (with outbox), tenancy column | An integration event crosses two modules; a tenant cannot read another's rows |
| M1-06 | Tests: aspire-fixture, red-first | Integration tests run against the real Aspire stack in CI |
| M1-07 | Frontend: react, spa, pnpm-workspaces, redux-toolkit, rtk-query-codegen, react-router, react-hook-form, zod, tailwind, shadcn, design-system none, tanstack-table, echarts, realtime none, oidc-client-ts, pwa no, accessibility | App builds, logs in through Keycloak, lists a module's data |
| M1-08 | Fabric half: agent briefs, `/next-issue` skill, board protocol workflows (janitor, return-to-Todo), cross-model review workflow, `docs/owners.yml` | Workflows tested; a dry run claims and releases an issue |
| M1-09 | Profile CI builds and tests the generated `saas-standard-react` project, backend and frontend | Green in this repo's CI |
| M1-10 | Pilot: one agent delivers one issue in a generated project | Merged PR in the pilot repo, delivered through the board protocol |

**Gates:** after M1-01 (the slot set, before any real pack is written)
and after M1-10 (the M1 gate above).
