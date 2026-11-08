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
| M2-03 | `saas-standard-vue`: vue, vue-router, pinia-tanstack-query, orval, vee-validate, reka-ui with tailwind (in place of primevue and library-theme, owner decision on #23), then shadcn-vue over Reka UI, with reka-ui kept as the headless option (#177); the shared frontend packs branch on the framework where their code differs (ADR-0006) | `build/profile-ci.sh saas-standard-vue` green |
| M2-04 | Event-driven backend: kafka, event-sourced modules on marten, physical CQRS | The backend of `industrial-event-driven-mfe` builds and its tests pass |
| M2-05 | Micro-frontends: module-federation, single framework, tokens-and-components, signalr | `build/profile-ci.sh industrial-event-driven-mfe` green |
| M2-06 | Profile CI runs every profile | One CI job per profile, all green |

**Gate after M2:** every profile green, and no pack had to edit another's
output (a slot was added instead). M2-00 and M2-01 are supervised; from M2-02
on, agents take the tasks through the board.

## M3 tasks

Every option that has a pack enters the pairwise matrix, and the matrix comes
first: generating the covering set over today's packs already fails 11 of 31
combinations and renders React files into 15 of 20 Vue projects. The owner's
decisions of 2026-11-06 set the scope; ADR-0003's amendment of that date
records the matrix domain, promotion and cadence.

Each task is one issue, one PR, tests first. Lanes are as in
`docs/owners.yml`; "after" names the tasks a task needs merged first.

### Phase 0: the matrix

| # | Task | Done when |
|---|---|---|
| M3-00 | Supervised: the M3 tasks here, ADR-0003 amended (matrix domain, promotion, cadence), `docs/catalog-rules.md` aligned with it; one issue per task with its lane label | Issues exist; M3-01 is claimable |
| M3-01 | Lane `generator`. `fabric matrix`: a deterministic pairwise covering set built with the catalog's own rule evaluator. Domain: every option with a pack. One module set per combination (the module defaults, on every module), plus explicit triples from `build/matrix-extra.yaml`. Written to `build/matrix.json`, with the infeasible pairs listed | Same catalog → byte-identical `matrix.json`; every feasible pair covered on the fixture catalog; a test that fails when the file is stale, seen failing |
| M3-02 | Lane `generator`. `fabric new` from a full choice set without a profile, and `build/profile-ci.sh --steps generate,build,test,web[,e2e]` | `profile-ci.sh` runs one `matrix.json` row with no profile and without e2e |
| M3-03 | Lane `ci`, after M3-01 and M3-02. Per-PR matrix job: generate every combination of `matrix.json` in one job, failing with the combination and its first error | Job in `ci.yml`, red on today's main; a non-required check until M3-05 and M3-06 are merged |
| M3-04 | Lane `ci`, after M3-01 and M3-02. Nightly and `workflow_dispatch` matrix workflow: one job per combination through build, unit, architecture and integration tests, and the frontend's build, lint and tests, without e2e; a table of duration and result per combination; a `bug` issue per failing supported option (ADR-0003) | A dispatch run produces the table; a deliberately broken combination opens one issue, and a rerun does not open a second |
| M3-05 | Lane `generator`. Catalog rules for the combinations packs refuse or render wrongly: Marten with tenancy other than none; physical CQRS on a module that is not event-sourced on Marten; module-federation and tokens-and-components with a framework other than React; library-theme with a headless or Tailwind-only library (shadcn, shadcn-vue, reka-ui, radix-headless) | `fabric validate` rejects each with one sentence, tests seen failing first; the per-PR matrix job has no generation failure from these |
| M3-06 | Lane `packs-frontend`. Vue branches for `frontend.state.tanstack-query` (vue-query) and `frontend.realtime.signalr` (a composable) | No `.tsx` file in a Vue combination with either; a pack test generates each with Vue and builds it |
| M3-07 | Lane `generator`. `fabric new`, `validate` and `add-module` warn about experimental options (ADR-0003) | The warning lists each chosen experimental option; test seen failing first |
| M3-08 | Lane `generator`, after M3-01, M3-04, M3-05 and M3-06. Tier check and first promotion: `SupportTierTests` fails when a `supported` option is in neither a profile nor `matrix.json`; every option whose matrix pairs all passed in one nightly run is promoted, starting with the 18 that already pass profile CI | Test seen failing first; each promotion cites its nightly run |
| M3-09 | Lane `ci`. CI capacity: a PR runs only the profile jobs whose packs it touches (all of them on `src/`, `build/` and base changes); the test shards are rebalanced | A frontend-only pack PR runs only the affected profiles; every shard within 15 min |

### Phase 1: catalog clean-up and vocabularies

The vocabularies are behaviour-preserving: their covering tests pass before
and after, unmodified.

| # | Task | Done when |
|---|---|---|
| M3-10 | Lane `generator`. Remove the dropped options from the catalog: `backend.bus.library.masstransit` and `backend.test.extras.contract-tests`. The tests that use them as stand-ins (`ChoiceResolverTests`, and the `ProbePack` in `MigrationTests`, `PersistenceInterceptorTests` and `ServiceDefaultsTests`) move to an option no pack will claim in M3 | `fabric validate` rejects each as an unknown option, test seen failing first; catalog self-check and pack tests green |
| M3-11 | Lane `packs-backend`. Test vocabulary: test attributes and assertions as `templates.functions` of the test-framework and assertion packs; every pack's generated tests use them | Every profile and matrix row has identical test results; no `#error … xUnit and Shouldly only` remains |
| M3-12 | Lane `packs-backend`, after M3-11. Integration-fixture vocabulary: the host client, the signed-in client and database access as slots or functions of the integration pack, used by rest, orval, rtk-query-codegen, roles, claims and rag | No `#error … aspire-fixture only` remains |
| M3-13 | Lane `packs-backend`. Identity-provider vocabulary: role and claim mapping, a test user's sign-in and the browser token as slots of the auth packs, used by roles, claims, signalr, ag-ui and the base policy rule | No `#error … Keycloak only` remains |

### Phase 2: packs, cheap first and cross-cutting last

Every pack task is done when the matrix rows that contain its option are
green, in addition to what its row says.

| # | Task | Done when |
|---|---|---|
| M3-20 | Lane `packs-backend`, after M3-04. `backend.database.sqlserver`: Aspire SQL Server, the EF provider, unique-constraint codes, Wolverine envelope storage, the transport-none outbox, tenancy column | `profile-ci.sh lean-crud --set backend.database=sqlserver` green |
| M3-21 | Lane `packs-backend`, after M3-20. `backend.data-access.reads.dapper` (was `ef-core-dapper-read`; owner decisions on #247): Dapper in query handlers, on both databases | Matrix rows green |
| M3-22 | Lane `packs-backend`, after M3-04. `backend.bus.transport.azure-service-bus` on the Aspire emulator | An integration event crosses two modules over the emulator |
| M3-23 | Lane `packs-backend`, after M3-04. `backend.mediator.wolverine`: `IDispatcher` over Wolverine's bus | Matrix rows green |
| M3-24 | Lane `packs-backend`, after M3-04. `backend.api-protocols.grpc` beside REST | A module's query answers over gRPC in an integration test |
| M3-25 | Lane `packs-backend`, after M3-13. `backend.auth.generic-oidc`, tested against Keycloak acting as a plain OIDC provider | Sign-in integration test green with generic-oidc |
| M3-26 | Lane `packs-backend`, after M3-13. `backend.auth.entra-id`, tested in CI against a stand-in issuer, without e2e | Token validation tested against the stand-in; the missing e2e recorded as a known limit in the pack's ADR |
| M3-27 | Lane `packs-backend`, after M3-04 and M3-05. `backend.tenancy.schema-per-tenant`: an EF schema per tenant; branches in rag and signalr; Marten is excluded by M3-05's rule | A tenant cannot read another's rows |
| M3-28 | Lane `packs-backend`, after M3-05. `module.event-store.kurrentdb` on Aspire; physical CQRS stays excluded by M3-05's rule | An event-sourced module round-trips through KurrentDB |
| M3-29 | Lane `packs-backend`, after M3-04. `module.communication.contract-interface`: synchronous interfaces in Contracts | Two modules call through a contract; the architecture rule still holds |
| M3-30 | Lane `packs-backend`, after M3-11. `backend.test.framework.nunit` | A profile with nunit green |
| M3-31 | Lane `packs-backend`, after M3-11. `backend.test.framework.tunit` | A profile with tunit green |
| M3-32 | Lane `packs-backend`, after M3-11. `backend.test.assertions.awesome-assertions` | A profile with awesome-assertions green |
| M3-33 | Lane `packs-backend`, after M3-11. `backend.test.mocks.moq` | A profile with moq green |
| M3-34 | Lane `packs-backend`, after M3-12. `backend.test.integration.testcontainers` | Integration tests green on Testcontainers |
| M3-35 | Lane `packs-backend`, after M3-04. `backend.test.extras.mutation-testing`: Stryker.NET, its CI job off by default like evals | Job generated; its gate tested with a surviving mutant |
| M3-40 | Lane `packs-frontend`, after M3-06. `frontend.state.zustand-tanstack-query` (React) | Matrix rows green |
| M3-41 | Lane `packs-frontend`, after M3-06. `frontend.state.pinia` (Vue) | Matrix rows green |
| M3-42 | Lane `packs-frontend`, after M3-04. `frontend.api-client.openapi-fetch` (role slots, ADR-0008) | Stale-document gate as orval's |
| M3-43 | Lane `packs-frontend`, after M3-04. `frontend.routing.tanstack-router` | Module routes discovered by glob, as with react-router |
| M3-44 | Lane `packs-frontend`, after M3-26. `frontend.auth-client.msal`, against M3-26's stand-in issuer, without e2e | Matrix rows green |
| M3-45 | Lane `packs-frontend`, after M3-04. `frontend.validation.valibot`, with resolver branches in react-hook-form and vee-validate | Matrix rows green |
| M3-46 | Lane `packs-frontend`, after M3-04. `frontend.forms.tanstack-form`, React and Vue | Matrix rows green |
| M3-47 | Lane `packs-frontend`, after M3-04. `frontend.forms.formkit` (Vue) | Matrix rows green |
| M3-48 | Lane `packs-frontend`, after M3-05. `frontend.styling.css-modules` | Matrix rows green |
| M3-49 | Lane `packs-frontend`, after M3-48. `frontend.ui-library.radix-headless` (React), with branches in the dashboards and ag-ui components | Matrix rows green |
| M3-50 | Lane `packs-frontend`, after M3-05. `frontend.ui-library.mui` and `frontend.styling.library-theme` | Matrix rows green |
| M3-51 | Lane `packs-frontend`, after M3-50. `frontend.ui-library.vuetify`, with library-theme's Vue branch | Matrix rows green |
| M3-52 | Lane `packs-frontend`, after M3-04. `frontend.data-grid.ag-grid`, community edition | Matrix rows green |
| M3-53 | Lane `packs-frontend`, after M3-04. `frontend.charts.recharts` (React) | Matrix rows green |
| M3-54 | Lane `packs-frontend`, after M3-04. `frontend.charts.chartjs`, React and Vue | Matrix rows green |
| M3-55 | Lane `packs-frontend`, after M3-13. `frontend.realtime.sse`: a host endpoint and its client | A server event reaches the browser in e2e |
| M3-56 | Lane `packs-frontend`, after M3-04. `frontend.pwa.yes` | Service worker registered; an offline-shell test |
| M3-57 | Lane `packs-frontend`, after M3-06. `frontend.design-system.tokens` (Style Dictionary) | React and Vue rows green |
| M3-58 | Lane `packs-frontend`, after M3-57. `frontend.design-system.web-components` | React and Vue rows green |
| M3-59 | Lane `packs-frontend`, after M3-06. `frontend.test.extras.storybook-tests` | Its gate fails on a broken story |
| M3-60 | Lane `packs-frontend`, after M3-06. `frontend.test.extras.screenshot-tests` | Its gate fails on a changed screenshot |
| M3-61 | Lane `packs-frontend`, after M3-04. `frontend.workspace.nx` | Matrix rows green |
| M3-62 | Lane `packs-frontend`, after M3-04. `frontend.workspace.turborepo` | Matrix rows green |
| M3-63 | Lane `packs-frontend`, after M3-06. `frontend.mfe-technique.native-federation` | Micro-frontend e2e green |
| M3-64 | Lane `packs-frontend`, after M3-58. `frontend.mfe-technique.web-components` | Micro-frontend e2e green |

### Phase 3: close

| # | Task | Done when |
|---|---|---|
| M3-70 | Lane `generator`, after M3-08 and every Phase 2 task. Promote every option whose matrix pairs pass | No experimental option with a pack remains, or each has a written reason |
| M3-71 | Lane `ci`, after M3-70. Two consecutive green nightly runs over the full matrix domain, linked from the issue | The M3 gate below |

**Gate after M3:** two consecutive green nightly matrix runs, and every
option in the matrix domain supported.

Phase 0 is on every later task's path; Phase 1's vocabularies come before
the test-library and auth packs, so those packs do not each edit the same
templates. The owner confirms the organisation's CI concurrency limit; M3-09
is done either way.

### Deferred and dropped

*Deferred:* the option stays in the catalog as experimental, has no pack, and
is outside the matrix domain. *Dropped:* the option leaves the catalog (M3-10).

| Option | Decision |
|---|---|
| `backend.topology.services` | Deferred: several hosts need an ADR of their own |
| `backend.api-style.controllers` | Deferred: every pack that produces an endpoint would branch on it |
| `backend.errors.exceptions` | Deferred: Result runs through dashboards, agents, MCP and the LLM sample |
| `backend.tenancy.database-per-tenant` | Deferred: it needs databases provisioned outside Aspire's static resources; schema-per-tenant is built (M3-27) |
| `frontend.architecture.multi-app` | Deferred until a pilot needs it and defines it |
| `frontend.mfe-mixed-frameworks.yes` | Deferred with multi-app |
| `frontend.ui-library.mantine`, `ant-design`, `fluent-ui`, `quasar`, `element-plus` | Deferred: each library needs branches in dashboards, ag-ui and the data grid. Kept: shadcn, shadcn-vue, reka-ui, radix-headless, mui, vuetify |
| `backend.bus.library.masstransit` | Dropped, without a licence check: Wolverine covers the role |
| `backend.test.extras.contract-tests` | Dropped: the client generated from OpenAPI, and CI failing on a stale document, cover most of its value |

Marten with tenancy other than none is not deferred but ruled out by the
catalog (M3-05), and built only if a pilot needs it.
