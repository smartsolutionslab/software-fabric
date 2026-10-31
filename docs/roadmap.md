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
| M1-06 | Tests: aspire-fixture, red-first. *Added after it:* guards, result style and absence as catalog options (Smart Sentinel Eye's conventions as defaults); EF Core migrations per module, applied by a MigrationRunner before the host starts | Integration tests run against the real Aspire stack in CI |
| M1-07 | Frontend: react, spa, pnpm-workspaces, redux-toolkit, rtk-query-codegen, react-router, react-hook-form, zod, tailwind, shadcn, design-system none, tanstack-table, echarts, realtime none, oidc-client-ts, pwa no, accessibility. *As built:* split in two — 07a the app shell (pnpm, react, spa, tailwind, shadcn, design-system none, router, react-hook-form, zod; the AppHost runs it with `AddViteApp`), 07b login, state, data and the remaining options. 07b also typed `ToHttpResult` (so the OpenAPI document describes responses) and gave `/me` and `/me/tenant` named response types; the frontend's `openapi.json` is checked in and kept current by an integration test | App builds, logs in through Keycloak, lists a module's data |
| M1-08 | Fabric half: agent briefs, `/next-issue` skill, board protocol workflows (janitor, return-to-Todo), cross-model review workflow, `docs/owners.yml`. *As built:* a base pack `base.fabric`; the protocol is one `gh` script per step in `.github/scripts/board/`, so every runtime uses the same steps; branches are parked rather than deleted (ADR-0004). Tested against a stand-in for `gh`; the review workflow's model actions have not run, since they need API keys | Workflows tested; a dry run claims and releases an issue |
| M1-09 | Profile CI builds and tests the generated `saas-standard-react` project, backend and frontend. *As built:* `build/profile-ci.sh` generates the profile with the CLI from the real packs (no stand-ins), adds a module and runs the generated CI's steps, a job of its own in this repo's CI; the generated CI gained an `e2e` job, whose script (`.github/scripts/e2e.sh`) the sign-in test runs too | Green in this repo's CI |
| M1-10 | Pilot: one agent delivers one issue in a generated project. *As built:* `smartsolutionslab/fabric-pilot`, generated by the CLI; its own CI (backend, frontend, e2e) green on GitHub. A fresh agent claimed issue #1 with `next.sh`, wrote the tests red first and opened PR #2; Codex reviewed it (no blocking findings), and it was merged on green CI. The pilot found three generator defects, each fixed here with a test seen failing: cloud sessions refuse GraphQL (scripts now use REST) and branch writes through the API (the lock now moves by `git push`, claim and release commits), and the domain building blocks assembly was misnamed | Merged PR in the pilot repo, delivered through the board protocol |

**Gates:** after M1-01 (the slot set, before any real pack is written)
and after M1-10 (the M1 gate above).

*M1 gate passed on 2026-10-31.* The user's review accepted: the board lock as
claim and release commits (ADR-0004), lanes and `agent:claimed` as labels with
the board optional, domain errors without HTTP (ADR-0007), the lockfile as the
first step after `fabric new`, and packs branching on other choices at
generation time (ADR-0006). Known limits carried into M2: `release.sh`,
`escalate.sh` and the janitor ran against the `gh` stand-in, not yet against
GitHub; `escalate.sh`'s `yq` expressions are tested against Python's `yq` only.

## M2 tasks

The three remaining profiles, generated from real packs and green in profile
CI. `fabric new` lists what each one lacks today: 7 packs for
`saas-standard-vue`, 12 for `industrial-event-driven-mfe`, 6 for `lean-crud`.

It also shows the first design question. The frontend's shared vocabulary
(`web.dependencies`, `web.imports`, `web.providers`, `web.env`,
`web.test-setup`, `web.restricted-imports`, `web.dev-dependencies`) is declared
by the React pack, and the request-header slots (`web.api-imports`,
`web.api-headers`) by `rtk-query-codegen`. Packs shared across frameworks
(zod, tanstack-table, echarts, oidc-client-ts, accessibility) contribute to
them, so choosing Vue or Orval leaves them nowhere to go.

| # | Task | Done when |
|---|---|---|
| M2-00 | The board protocol in this repo: labels, lanes (`generator`, `packs-backend`, `packs-frontend`, `ci`), `docs/owners.yml`, the janitor and return-to-Todo workflows, one issue per M2 task | An agent claims an M2 issue here with `next.sh` |
| M2-01 | Role slots: a slot that several options of one dimension must all declare, with one shared description (frontend framework, API client). Decision and ADR first (human), then the generator check | Composing a profile fails, naming the option, when a role slot is missing from it |
| M2-02 | `lean-crud`: transport none, tenancy none, module style crud, CQRS none, state tanstack-query, API client orval | `build/profile-ci.sh lean-crud` green |
| M2-03 | `saas-standard-vue`: vue, vue-router, pinia-tanstack-query, orval, vee-validate, primevue, library-theme; the shared frontend packs branch on the framework where their code differs (ADR-0006) | `build/profile-ci.sh saas-standard-vue` green |
| M2-04 | Event-driven backend: kafka, event-sourced modules on marten, physical CQRS | The backend of `industrial-event-driven-mfe` builds and its tests pass |
| M2-05 | Micro-frontends: module-federation, single framework, tokens-and-components, signalr | `build/profile-ci.sh industrial-event-driven-mfe` green |
| M2-06 | Profile CI runs every profile | One CI job per profile, all green |

**Gate after M2:** every profile green, and no pack had to edit another's
output (a slot was added instead). M2-00 and M2-01 are supervised; from M2-02
on, agents take the tasks through the board.
