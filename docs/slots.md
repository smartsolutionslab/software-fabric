# The slot set (M1)

What a generated project looks like, which pack owns which file, and the
slots through which packs reach into files they do not own. Written for
the 37 packs of `saas-standard-react` plus the base; later profiles add
slots in their own change (ADR-0002).

**Status: accepted at the M0 gate (2026-10-30).** Generator changes 1–3
below are built (M1-01). Building the base (M1-02) added four slots and
two conventions, marked *added in M1-02* below.

## Layout of a generated project

```
fabric.yaml                     generator
README.md  CLAUDE.md            base
AGENTS.md                       base; every pack's agents-md via agents.sections
.claude/agents/<role>.md        base; every pack's brief for that role via agents.brief.<role>
docs/adr/NNNN-<dimension>-<option>.md
                                generator; one per pack, numbered in selection order
global.json  .editorconfig  Directory.Build.props  Directory.Packages.props  build.proj
Solution.slnx                   base
.config/dotnet-tools.json       base; tools by slot (dotnet-ef from ef-core)
build/guards/BannedSymbols.txt  backend.guards.ensure-that
src/
  AppHost/                      base (Aspire composition root)
  ServiceDefaults/              base
  BuildingBlocks/               base; packs add their own files under it
  Contracts/                    base
  Kernel/                       base (project); guards, result and absence packs add Ensure, Result, ApiError, Option
  Host/                         backend.topology.modular-monolith
  MigrationRunner/              backend.data-access.ef-core (applies every module's migrations, then exits)
  Modules/
    Directory.Build.props       base; per-layer package references by slot
    <Module>/<Module>.Domain|Application|Infrastructure|Api/
                                base owns the csproj files and <Module>Module.cs
                                (per module); module.style / module.cqrs / ... own the code
tests/
  Directory.Build.props         base; test packages and usings by slot
  Architecture.Tests/           base; packs add gate tests as their own files
  Integration.Tests/            backend.test.integration.aspire-fixture
  Modules/<Module>.Tests/       base (per module)
web/
  package.json  pnpm-workspace.yaml   frontend.workspace.pnpm-workspaces
  app/                          frontend.framework.react
.github/workflows/ci.yml        base; steps by slot
```

## Modules register by convention, never by list

`fabric add-module` only creates files (ADR-0001), so nothing may need a
line per module in a shared file. Proposed in ADR-0005:

- **Build:** `build.proj` (Microsoft.Build.Traversal) and the Host pick up
  module projects with globs — `src/Modules/*/*.Api/*.Api.csproj` — not a
  list.
- **Runtime:** each module has one `<Module>Module.cs` implementing
  `IModule` from BuildingBlocks; the Host discovers modules by assembly
  scan and calls their registration.
- **Packages per layer:** `src/Modules/Directory.Build.props` adds
  package references by project-name suffix (`.Domain`, `.Infrastructure`,
  …), so a pack reaches every module's layer through one project-level
  slot.
- **Per-module code stays generic:** a pack that needs something in every
  module (a DbContext, a tenant filter, handler discovery) puts a generic
  implementation in BuildingBlocks and lets the per-module file inherit
  from it. **No per-module slots** — the generator does not need them.
- **Frontend modules** are found with `import.meta.glob('./modules/*/routes.tsx')`.
- **Solution file:** `Solution.slnx` lists the projects at generation time;
  `add-module` prints the `dotnet sln add` line instead of editing it.
  `build.proj` is what CI builds, so a stale `.slnx` never breaks a build.

## Architecture rules are framework-free (added in M1-02)

A gate of kind `architecture-test` is a class implementing
`IArchitectureRule` (base, `tests/Architecture.Tests`), placed in
`tests/Architecture.Tests/Rules/` as the pack's own file. Rules depend on
no test framework; the chosen test-framework pack contributes the one
test that runs every rule. A pack's gate therefore works with xUnit,
NUnit and TUnit alike.

Tests a test-framework pack generates assert through slots the
assertion pack fills (added in M1-04), so they read like every other
test in the project without the two packs knowing each other.

## Module paths use the namespace (added in M1-02)

Per-module targets use `{{ module.namespace }}`: module `order-handling`
lives in `src/Modules/OrderHandling/OrderHandling.Domain/`, its
assemblies are `<Namespace>.Modules.OrderHandling.<Layer>`, and the host
loads `<Namespace>.Modules.*.Api.dll`.

## Wiring code lives in extension methods (added in M1-03)

A contribution to `host.services`, `host.middleware` or
`modules.infrastructure` is a call to an extension method the pack owns,
declared in `Microsoft.Extensions.DependencyInjection` (services) or
`Microsoft.AspNetCore.Builder` (app). Shared files then need no `using`
per choice, and the code behind the call is in a file the pack owns.

That file's target is a `Wiring/` folder inside the project that holds the
extension (`src/BuildingBlocks/Api/Wiring/EndpointDiscovery.cs`,
`src/Host/Wiring/Authentication.cs`; added in #39). Generated projects
enforce namespaces that match the folder (IDE0130) everywhere except
`src/**/Wiring/*.cs`, so wiring code is the only code in a `Microsoft.*`
namespace, and a pack that puts one anywhere else fails the build.

## Slots

Cardinality `0+` unless stated. "Separator" is what joins contributions
(new, see Generator changes); default is a newline.

### Agent setup and docs

| Slot | File | Separator | Filled by (saas-standard-react) |
|---|---|---|---|
| `agents.sections` | `AGENTS.md` | blank line | **generator**: every selected pack's `agents-md`, in selection order |
| `agents.brief.<role>` | `.claude/agents/<role>.md` | blank line | **generator**: every pack's brief for that role |
| `readme.sections` | `README.md` | blank line | packs that change how to run the project (aspire, pnpm, keycloak) |

### .NET build

| Slot | File | Filled by |
|---|---|---|
| `templates.functions` *(added with the kernel options)* | — (Scriban code, prefixed to every template) | backend.guards.* (`not_null`, `not_blank`) |
| `dotnet.build-items` *(added with the kernel options)* | `Directory.Build.props` | ensure-that (ban list), typed-outcomes (`using static Result`) |
| `dotnet.tools` *(added with migrations)* | `.config/dotnet-tools.json` | ef-core (`dotnet-ef`) |
| `dotnet.package-versions` | `Directory.Packages.props` | every pack that brings a NuGet package |
| `dotnet.build-properties` | `Directory.Build.props` | packs adding analyzers or properties (result, red-first) |
| `solution.projects` *(added in M1-02)* | `Solution.slnx` | packs owning a project: modular-monolith (Host), aspire-fixture (Integration.Tests), ensure-that, option and the result styles (their tests under `tests/Kernel/`) |
| `modules.packages.domain` | `src/Modules/Directory.Build.props` | — (Domain stays dependency-free) |
| `modules.packages.application` | 〃 | wolverine, mediator |
| `modules.packages.infrastructure` | 〃 | ef-core, postgres, wolverine |
| `modules.packages.api` | 〃 | minimal-apis |
| `buildingblocks.packages` | `src/BuildingBlocks/BuildingBlocks.csproj` | ef-core, wolverine, result |
| `tests.build-properties` *(added in M1-02)* | `tests/Directory.Build.props` | xunit (`OutputType Exe` for xUnit v3) |
| `tests.packages` | `tests/Directory.Build.props` | xunit, shouldly, nsubstitute, aspire-fixture |
| `tests.usings` | `tests/Directory.Build.props` | xunit, shouldly, nsubstitute |
| `tests.assert-no-violations` *(added in M1-04, declared by the test-framework pack, exactly-one)* | `tests/Architecture.Tests/ArchitectureRulesTests.cs` | shouldly (`violations.ShouldBeEmpty(description);`) |
| `tests.assert-does-not-throw` *(added in M1-04, declared by the test-framework pack, exactly-one)* | `tests/Modules/<Module>.Tests/<Module>ModuleTests.cs` | shouldly (`Should.NotThrow(buildContainer);`) |

**One package, one owner.** A `PackageVersion` is declared by exactly
one pack; others only reference it. Two packs declaring the same version
is a defect in the second pack.

### Composition root and host

| Slot | File | Cardinality | Filled by |
|---|---|---|---|
| `apphost.packages` | `src/AppHost/AppHost.csproj` | 0+ | postgres, rabbitmq, keycloak, react |
| `apphost.project-references` *(added in M1-02)* | `src/AppHost/AppHost.csproj` | 0+ | backend.topology (the host project) |
| `apphost.resources` | `src/AppHost/Program.cs` | 0+ | postgres, rabbitmq, keycloak, react (Vite app) |
| `apphost.host` | `src/AppHost/Program.cs` | **exactly-one** | backend.topology (adds the host project) |
| `apphost.host-wiring` | `src/AppHost/Program.cs` | 0+ | postgres, rabbitmq, keycloak (`.WithReference(...).WaitFor(...)`) |
| `apphost.web` *(added in M1-07)* | `src/AppHost/Program.cs` | at-most-one | react (`AddViteApp("web", "../../web/app").WithPnpm()`) |
| `apphost.web-wiring` *(added in M1-07)* | `src/AppHost/Program.cs` | 0+ | keycloak, the host (`.WithReference(...)`) |
| `host.packages` | `src/Host/Host.csproj` | 0+ | keycloak, wolverine, ef-core |
| `host.services` | `src/Host/Program.cs` | 0+ | keycloak, wolverine (with `modules` in scope), result (problem details, the guard exception handler), tenancy |
| `host.middleware` | `src/Host/Program.cs` | 0+ | result (`UseExceptionHandler`, first), keycloak, tenancy |
| `host.endpoints` *(added in M1-06)* | `src/Host/Program.cs` | 0+ | keycloak (`/me`), tenancy (`/me/tenant`) |
| `modules.infrastructure` | `src/BuildingBlocks/Modules/ModuleInfrastructure.cs` | 0+ | ef-core, wolverine, tenancy |
| `modules.endpoints` *(added in M1-02)* | `src/BuildingBlocks/Modules/ModuleEndpoints.cs` | 0+ | minimal-apis (maps `IEndpoint`s), keycloak (`group.RequireAuthorization()`) |
| `persistence.options` *(added in M1-03)* | `PersistenceRegistration.cs` and each module's design-time factory (owned and declared by ef-core); `options`, `connectionString` and `schema` in scope | **exactly-one** | postgres (`options.UseNpgsql(connectionString, ...)` with the history table in `schema`) |
| `persistence.model-conventions` | `src/BuildingBlocks/Persistence/ModuleDbContext.cs` (owned and declared by ef-core) | 0+ | tenancy column |
| `persistence.context-members` *(added in M1-05)* | `src/BuildingBlocks/Persistence/ModuleDbContext.cs` | 0+ | tenancy column (`CurrentTenantId`) |
| `persistence.interceptors` *(added in #33; separator `,` + newline)* | `src/BuildingBlocks/Persistence/ModuleDbContext.cs` (`OnConfiguring`, rendered only with a contribution); `Services` in scope | 0+ | ddd (dispatches domain events after the save, or after the commit of a transaction) |
| `persistence.save-changes` *(added in M1-05)* | `src/BuildingBlocks/Persistence/ModuleDbContext.cs` | 0+ | tenancy column (stamps new rows) |
| `migration-runner.steps` *(added in #29; declared by ef-core)* | `src/MigrationRunner/Program.cs`, after every module's EF Core migrations; `IHost host` in scope, `await` allowed | 0+ | an event store's schema (Marten, #28); empty, the runner is unchanged |

### Frontend

| Slot | File | Separator | Filled by |
|---|---|---|---|
| `web.workspace-packages` *(added in M1-07)* | `web/pnpm-workspace.yaml` | newline | spa (`  - app`) |
| `web.dependencies` | `web/app/package.json` | `,` + newline | react, redux, rtk, router, rhf, zod, tailwind, shadcn, tanstack-table, echarts, oidc |
| `web.dev-dependencies` | 〃 | `,` + newline | vitest, testing-library, playwright, axe, rtk codegen |
| `web.scripts` | 〃 | `,` + newline | rtk-query-codegen (`codegen`) |
| `web.prepare` *(added in M1-07b)* | 〃 (`dev`, `build`, `test`) | ` && ` | rtk-query-codegen (`pnpm codegen`) |
| `web.env` *(added in M1-07b)* | `web/app/src/vite-env.d.ts` | newline | oidc-client-ts (`VITE_OIDC_AUTHORITY`) |
| `web.test-setup` *(added in M1-07b)* | `web/app/src/test-setup.ts` | newline | accessibility (axe matchers) |
| `web.lint-ignores` *(added in M1-07b)* | `web/app/eslint.config.js` | `,` + newline | rtk-query-codegen (the generated client) |
| `web.format-ignores` *(added for #38)* | `web/app/.prettierignore` | newline | rtk-query-codegen, orval (the generated client, `openapi.json`) |
| `web.store-imports`, `web.store-reducers`, `web.store-middleware` *(added in M1-07b; declared by redux-toolkit)* | `web/app/src/store.ts` | newline / `,` + newline / none | rtk-query-codegen |
| `web.api-imports`, `web.api-headers` *(added in M1-07b; declared by every api-client pack)* | `web/app/src/api/baseApi.ts` | newline | oidc-client-ts (the bearer token) |
| `web.vite-imports` *(added in M1-07)* | `web/app/vite.config.ts` | newline | tailwind |
| `web.vite-plugins` | `web/app/vite.config.ts` | `,` + newline | tailwind |
| `web.imports` | `web/app/src/main.tsx` | newline | redux, router, oidc |
| `web.providers` | `web/app/src/main.tsx` | `,` + newline | redux, oidc, router — wrappers composed in order |
| `web.styles` | `web/app/src/index.css` | newline | tailwind, shadcn |
| `web.restricted-imports` *(added in M1-07)* | `web/app/eslint.config.js` | `,` + newline | router, shadcn, react-hook-form, zod — each bans its alternatives |

### CI

| Slot | File | Filled by |
|---|---|---|
| `ci.backend-steps` | `.github/workflows/ci.yml` | xunit (runs every test project with code coverage, then the coverage gate; the integration tests need Docker, which GitHub's runners have) |
| `pr-template.sections` *(added in M1-06)* | `.github/pull_request_template.md` | fabric (`## Spec and plan`), red-first (`## Tests observed failing`) |
| `ci.frontend-steps` | 〃 | pnpm, react (`pnpm format:check`), rtk-query-codegen (generated client is current), accessibility |
| `readme.first-steps` *(added for #11)* | `README.md` (`## First steps`, printed by `fabric new`) | pnpm (create and commit `web/pnpm-lock.yaml`) |
| `ci.e2e-steps` *(added in M1-09)* | `.github/workflows/ci.yml` | react (builds, then `.github/scripts/e2e.sh` starts the AppHost and runs Playwright); without a contribution there is no e2e job |

## Generator changes this needs

1. **Slot separators.** `declares-slots` gains `separator` (default
   newline). JSON lists and object entries need `,` between contributions
   and none after the last; templates cannot know which contribution is
   last.
2. **The generator writes what every pack already declares.** Today
   `adr`, `agents.agents-md` and `agents.briefs` are validated but not
   used. The generator renders each pack's ADR to
   `docs/adr/NNNN-<dimension>-<option>.md` (numbered in selection order)
   and contributes `agents-md` and briefs to the reserved slots
   `agents.sections` and `agents.brief.<role>`, which the base declares.
   Packs may not contribute to those slots themselves.
3. **A project name.** Templates need `name` (lower kebab-case, from the
   target directory unless `--name` is given) and `namespace`
   (PascalCase). Recorded in `fabric.yaml`.

## Known open point

`frontend.framework.react` owns `web/app/`. That fits a single SPA; for
`multi-app` and `micro-frontends` (M2) the app layout belongs to the
architecture pack and the framework pack must contribute into it. That
recut is M2's first task, not a reason to design for it now.
