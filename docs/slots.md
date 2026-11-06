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
build/time/BannedSymbols.txt    base (DateTime.Now and friends; the time comes from TimeProvider)
build/ids/BannedSymbols.txt     base (Guid.NewGuid in src/ and tests/; every Guid is Guid.CreateVersion7())
src/
  AppHost/                      base (Aspire composition root)
  ServiceDefaults/              base
  BuildingBlocks/               base; packs add their own files under it
  Contracts/                    base
  Kernel/                       base (project; Page, PageSize, PagedResult, Sorting); guards, result and absence packs add Ensure, Result, ApiError, Option
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
  Integration.Tests/            backend.test.integration.aspire-fixture; the REST pack adds ListEndpointsArePagedTests
  Kernel/Paging.Tests/          base; the guards, absence and result packs add their own Kernel test projects
  Modules/<Module>.Tests/       base (per module)
web/
  package.json  pnpm-workspace.yaml   frontend.workspace.pnpm-workspaces
  app/                          frontend.framework.react
  modules/<module>/             frontend.mfe-technique.module-federation (per module: the module's remote; micro-frontends only)
.github/workflows/ci.yml        base; steps by slot
.github/dependabot.yml          base; an entry per package ecosystem by slot
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
(new, see Generator changes); default is a newline. A contribution that
renders empty or whitespace-only is dropped before the join, so it adds no
separator (added in #221).

A contribution to a slot no selected pack declares is an error, unless
it carries `optional: true` (added for #134): then it is dropped when the
slot is undeclared and renders as usual when it is declared. That lets a
pack fill a slot of a choice it can be combined with but does not need
(docs/packs.md, ADR-0002).

### Agent setup and docs

| Slot | File | Separator | Filled by (saas-standard-react) |
|---|---|---|---|
| `agents.sections` | `AGENTS.md` | blank line | **generator**: every selected pack's `agents-md`, in selection order |
| `agents.brief.<role>` | `.claude/agents/<role>.md` | blank line | **generator**: every pack's brief for that role |
| `readme.sections` | `README.md` | blank line | packs that change how to run the project (aspire, pnpm, keycloak) |

### .NET build

| Slot | File | Filled by |
|---|---|---|
| `templates.functions` *(added with the kernel options)* | — (Scriban code, prefixed to every template) | backend.guards.* (`not_null`, `not_blank`); the test-framework and assertion packs (the test vocabulary below, M3-11) |
| `dotnet.build-items` *(added with the kernel options)* | `Directory.Build.props` | ensure-that (ban list), typed-outcomes (`using static Outcome`) |
| `dotnet.tools` *(added with migrations)* | `.config/dotnet-tools.json` | ef-core (`dotnet-ef`) |
| `dotnet.package-versions` | `Directory.Packages.props` | every pack that brings a NuGet package |
| `dotnet.build-properties` | `Directory.Build.props` | packs adding analyzers or properties (result, red-first) |
| `solution.projects` *(added in M1-02)* | `Solution.slnx` | packs owning a project: modular-monolith (Host), aspire-fixture (Integration.Tests), ensure-that, option and the result styles (their tests under `tests/Kernel/`) |
| `modules.packages.domain` | `src/Modules/Directory.Build.props` | — (Domain stays dependency-free) |
| `modules.packages.application` | 〃 | wolverine, mediator, agent-framework (`Microsoft.Agents.AI`: an agent lives in Application, ADR-0011) |
| `modules.packages.infrastructure` | 〃 | ef-core, postgres, wolverine |
| `modules.packages.api` | 〃 | minimal-apis |
| `buildingblocks.packages` | `src/BuildingBlocks/BuildingBlocks.csproj` | ef-core, wolverine, result |
| `tests.build-properties` *(added in M1-02)* | `tests/Directory.Build.props` | xunit (`OutputType Exe` for xUnit v3) |
| `tests.packages` | `tests/Directory.Build.props` | xunit, shouldly, nsubstitute, aspire-fixture |
| `tests.usings` | `tests/Directory.Build.props` | xunit, shouldly, nsubstitute |
| `tests.architecture.references` *(added with #50)* | `tests/Architecture.Tests/Architecture.Tests.csproj` | keycloak (the host and `Microsoft.AspNetCore.Mvc.Testing`, for its endpoint rule) |
| `tests.module-host-services` *(added for #77, declared by the test-framework pack)* | `tests/Modules/<Module>.Tests/<Module>ModuleTests.cs`, before the module's services; `services` in scope | the base, with a model provider (the tests' stand-in `IChatClient`, which the host otherwise provides); agent-framework (`IDispatcher`, which a handler running an agent takes, and `IAgentMcpTools` with the MCP client) |
| `integration-tests.parameters` *(added for #77, declared by aspire-fixture)* | `tests/Integration.Tests/AspireFixture.cs`, before the application is built; `builder` in scope | a model provider with a key (a key-shaped value no provider accepts, for its secret parameter); agent-ui ag-ui (`model-stand-in` true: the host's stand-in model answers the agents) |

#### The test vocabulary *(M3-11)*

A pack's generated tests never name the test framework or the assertion
library: they call `templates.functions` that every pack of those two
dimensions defines, so another option plugs in by defining the same
functions instead of editing every pack's tests (owner decision D6).
Arguments are C# code as Scriban strings, best as backtick verbatim
strings (no escapes, may span lines); each function returns C# code
without a trailing `;`. `TestVocabularyTests` fails when a pack of either
dimension defines more or fewer functions, when a pack's template names
xUnit or Shouldly directly, or when a template refuses another option
with `#error`. The framework and assertion packs' own files may use their
library directly.

| Function | Returns (xUnit v3 / Shouldly) |
|---|---|
| `test_attribute` | `[Fact]` |
| `test_cases_attribute` | `[Theory]` |
| `test_case_attribute arguments` | `[InlineData(arguments)]` |
| `test_cancellation_token` | `TestContext.Current.CancellationToken` |
| `write_test_output text` | `TestContext.Current.TestOutputHelper?.WriteLine(text)` |
| `actual \| should_be expected [message]` | `actual.ShouldBe(expected)`; also `should_be_ignoring_order` (sequences), `should_contain`, `should_not_contain`, `should_start_with`, `should_be_greater_than`, `should_all_be` (a predicate), `should_be_subset_of`, `should_be_same_as` |
| `actual \| should_be_true [message]` | `actual.ShouldBeTrue()`; also `should_be_false`, `should_be_null`, `should_not_be_null`, `should_be_empty`, `should_not_be_empty`, `should_be_in_order` |
| `actual \| should_be_in_range from to` | `actual.ShouldBeInRange(from, to)` |
| `actual \| should_have_single_item` | the single item |
| `actual \| should_be_of_type type`, `should_be_assignable_to type` | the value as `type` |
| `should_throw type action`, `should_throw_async type action` | the exception (awaitable for the async one) |
| `should_not_throw action` | `Should.NotThrow(action)` |

Scriban indents every later line of a multi-line result by the
indentation of the line the call starts, when nothing but whitespace
precedes it there: write a multi-line argument's later lines that much
less indented than they should come out.

Every assertion renders after its `actual`, so `actual` may continue the
line above (`` `.Message` ``) as in a fluent chain. A template writes
`{{ `total` | should_be `42` }};` for `total.ShouldBe(42);`. Fixtures and
their lifetimes (the Aspire fixture, the rag tests' database) are the
integration pack's vocabulary, not this one (M3-12).

**One package, one owner.** A `PackageVersion` is declared by exactly
one pack; others only reference it. Two packs declaring the same version
is a defect in the second pack.

### Composition root and host

| Slot | File | Cardinality | Filled by |
|---|---|---|---|
| `apphost.packages` | `src/AppHost/AppHost.csproj` | 0+ | postgres, rabbitmq, keycloak, react |
| `apphost.project-references` *(added in M1-02)* | `src/AppHost/AppHost.csproj` | 0+ | backend.topology (the host project) |
| `apphost.resources` | `src/AppHost/Program.cs` | 0+ | postgres, rabbitmq, keycloak, react (Vite app), micro-frontends (a Vite app per directory of `web/modules`, `builder.AddRemotes()`), agent-ui ag-ui (the `model-stand-in` parameter, false unless a test sets it) |
| `apphost.host` | `src/AppHost/Program.cs` | **exactly-one** | backend.topology (adds the host project) |
| `apphost.host-wiring` | `src/AppHost/Program.cs` | 0+ | postgres, rabbitmq, keycloak (`.WithReference(...).WaitFor(...)`), agent-ui ag-ui (`model-stand-in` as `Agents__ModelStandIn`) |
| `apphost.web` *(added in M1-07)* | `src/AppHost/Program.cs` | at-most-one | react (`AddViteApp("web", "../../web/app").WithPnpm()`) |
| `apphost.web-wiring` *(added in M1-07)* | `src/AppHost/Program.cs` | 0+ | keycloak, the host (`.WithReference(...)`), micro-frontends (`.WithRemotes(remotes)`: each remote's address) |
| `service-defaults.activity-sources` *(added for #205; separator `, `)* | `src/ServiceDefaults/Wiring/Extensions.cs`, one more `.AddSource(...)` on the tracer, rendered only with a contribution | 0+ | C# string expressions naming a library's `ActivitySource`, whose spans exist only while something listens: agent-ui ag-ui (`"Experimental.AGUI.Server"`, the AG-UI run spans that carry a mid-stream failure) |
| `host.packages` | `src/Host/Host.csproj` | 0+ | keycloak, wolverine, ef-core, agent-ui ag-ui (`AGUI.Server`, `AGUI.Formatting`: only the host speaks AG-UI) |
| `host.services` | `src/Host/Program.cs` | 0+ | keycloak, wolverine (with `modules` in scope), result (problem details, the guard exception handler), tenancy, ef-core (the unique-constraint exception handler), agent-ui ag-ui (the agents by name, and in Development the stand-in model; after the provider's client, order 40) |
| `host.middleware` | `src/Host/Program.cs` | 0+ | result (`UseExceptionHandler`, first), keycloak, tenancy |
| `host.endpoints` *(added in M1-06)* | `src/Host/Program.cs` | 0+ | keycloak (`/me`), tenancy (`/me/tenant`), agent-ui ag-ui (`POST /agents/{name}`) |
| `host.log-levels` | `src/Host/appsettings.json`, under `Logging:LogLevel` | 0+ | ef-core (SQL commands at `Warning`) |
| `modules.infrastructure` | `src/BuildingBlocks/Modules/ModuleInfrastructure.cs` | 0+ | ef-core, wolverine, tenancy, agent-ui ag-ui (registers the module's `IAgentConversation<TSelf>` queries by their agents' names) |
| `modules.endpoints` *(added in M1-02)* | `src/BuildingBlocks/Modules/ModuleEndpoints.cs` | 0+ | minimal-apis (maps `IEndpoint`s), keycloak (`group.RequireAuthorization()`) |
| `persistence.options` *(added in M1-03)* | `PersistenceRegistration.cs` and each module's design-time factory (owned and declared by ef-core); `options`, `connectionString` and `schema` in scope | **exactly-one** | postgres (`options.UseNpgsql(connectionString, ...)` with the history table in `schema`) |
| `persistence.model-conventions` | `src/BuildingBlocks/Persistence/ModuleDbContext.cs` (owned and declared by ef-core) | 0+ | tenancy column |
| `persistence.context-members` *(added in M1-05)* | `src/BuildingBlocks/Persistence/ModuleDbContext.cs` | 0+ | tenancy column (`CurrentTenantId`) |
| `persistence.interceptors` *(added in #33; separator `,` + newline)* | `src/BuildingBlocks/Persistence/ModuleDbContext.cs` (`OnConfiguring`, rendered only with a contribution); `Services` in scope | 0+ | ddd (dispatches domain events after the save, or after the commit of a transaction) |
| `persistence.save-changes` *(added in M1-05)* | `src/BuildingBlocks/Persistence/ModuleDbContext.cs` | 0+ | tenancy column (stamps new rows) |
| `migration-runner.steps` *(added in #29; declared by ef-core)* | `src/MigrationRunner/Program.cs`, after every module's EF Core migrations; `IHost host` in scope, `await` allowed | 0+ | an event store's schema (Marten, #28); empty, the runner is unchanged |
| `event-store.json-converters` *(added for #121; declared by marten)* | `src/BuildingBlocks/EventStore/ModuleEventStore.cs` (the System.Text.Json options of every module's Marten store, `JsonSerializerOptions json` in scope; rendered only with a contribution) | 0+ | module.ddd-depth.strict, `optional: true` (writes value objects as their backing values) |

### Frontend

| Slot | File | Separator | Filled by |
|---|---|---|---|
| `web.workspace-packages` *(added in M1-07)* | `web/pnpm-workspace.yaml` | newline | spa (`  - app`), micro-frontends (`app`, `modules/*`), design-system tokens-and-components (`packages/tokens`, `packages/ui`) |
| `web.dependencies` | `web/app/package.json` | `,` + newline | react, redux, rtk, router, rhf, zod, tailwind, shadcn, tanstack-table, echarts, oidc, agent-ui ag-ui (`@ag-ui/client`) |
| `web.dev-dependencies` | 〃 | `,` + newline | vitest, testing-library, playwright, axe, rtk codegen |
| `web.scripts` | 〃 | `,` + newline | rtk-query-codegen (`codegen`) |
| `web.prepare` *(added in M1-07b)* | 〃 (`dev`, `build`, `test`) | ` && ` | rtk-query-codegen (`pnpm codegen`), design-system tokens-and-components (builds the tokens) |
| `web.env` *(added in M1-07b)* | `web/app/src/vite-env.d.ts` | newline | oidc-client-ts (`VITE_OIDC_AUTHORITY`) |
| `web.test-setup` *(added in M1-07b)* | `web/app/src/test-setup.ts` | newline | accessibility (axe matchers) |
| `web.lint-ignores` *(added in M1-07b)* | `web/app/eslint.config.js` | `,` + newline | rtk-query-codegen (the generated client) |
| `web.format-ignores` *(added for #38)* | `web/app/.prettierignore` | newline | rtk-query-codegen, orval (the generated client, `openapi.json`) |
| `web.typecheck-excludes` *(added for #126; declared by react)* | `web/app/tsconfig.json` (`exclude`, rendered only with a contribution) | `, ` | rtk-query-codegen (the generated client, without keycloak: its document starts with no paths) |
| `web.store-imports`, `web.store-reducers`, `web.store-middleware` *(added in M1-07b; declared by redux-toolkit)* | `web/app/src/store.ts` | newline / `,` + newline / none | rtk-query-codegen |
| `web.api-imports`, `web.api-headers` *(added in M1-07b; declared by every api-client pack)* | `web/app/src/api/baseApi.ts` | newline | oidc-client-ts (the bearer token) |
| `web.openapi-paths`, `web.openapi-schemas` *(added for #184; role slots of `frontend.api-client`)* | `web/app/openapi.json` (`paths` after the host's own; `components.schemas`, sorted by name with the host's own) | `,` + newline / blank line | frontend.dashboards.user-editable (the dashboards module's endpoints) |
| `web.vite-imports` *(added in M1-07)* | `web/app/vite.config.ts` | newline | tailwind |
| `web.vite-plugins` | `web/app/vite.config.ts` | `,` + newline | tailwind, module-federation (`federation(shellFederation)`) |
| `web.imports` | `web/app/src/main.tsx` | newline | redux, router, oidc |
| `web.providers` | `web/app/src/main.tsx` | `,` + newline | redux, oidc, router — wrappers composed in order |
| `web.styles` | `web/app/src/index.css` | newline | tailwind, shadcn, design-system tokens-and-components (the tokens' Tailwind theme, `@source` for the ui package), micro-frontends (`@source` for the remotes) |
| `web.restricted-imports` *(added in M1-07)* | `web/app/eslint.config.js` | `,` + newline | router, shadcn, react-hook-form, zod — each bans its alternatives; micro-frontends (the shell imports no remote); agent-ui none (no AG-UI or CopilotKit client), agent-ui ag-ui (no CopilotKit) |
| `web.remote-restricted-imports` *(added for #156; declared by module-federation)* | `web/modules/<module>/eslint.config.js` | `,` + newline | mfe-mixed-frameworks no (bans every UI framework but the shell's) |

### CI

| Slot | File | Filled by |
|---|---|---|
| `ci.backend-steps` | `.github/workflows/ci.yml` | xunit (runs every test project with code coverage, then the coverage gate; the integration tests need Docker, which GitHub's runners have) |
| `pr-template.sections` *(added in M1-06)* | `.github/pull_request_template.md` | fabric (`## Spec and plan`), red-first (`## Tests observed failing`, `## Tests observed passing before the change`) |
| `ci.frontend-steps` | 〃 | pnpm, react (`pnpm format:check`), rtk-query-codegen (generated client is current), accessibility |
| `readme.first-steps` *(added for #11)* | `README.md` (`## First steps`, printed by `fabric new`) | pnpm (create and commit `web/pnpm-lock.yaml`) |
| `e2e.apphost-arguments` *(added for #203; a role slot of `frontend.framework` since #208, separator a space)* | `.github/scripts/e2e.sh` (`dotnet run ... -- <contributions>`; rendered only with a contribution) | anthropic, openai, azure-openai (`optional: true`: the key-shaped values the AspireFixture gives them, without which the AppHost waits for its secret parameters); agent-ui ag-ui (`--Parameters:model-stand-in=true`, order 90: the host's stand-in model answers the agents) |
| `ci.e2e-steps` *(added in M1-09)* | `.github/workflows/ci.yml` | react (builds, then `.github/scripts/e2e.sh` starts the AppHost and runs Playwright); without a contribution there is no e2e job |
| `dependabot.updates` *(added for #55)* | `.github/dependabot.yml` (base writes the NuGet and GitHub Actions entries) | pnpm (npm for `/web`); a pack that adds a package ecosystem adds its entry here |

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

*As built for `micro-frontends` (#156):* no recut was needed. `web/app` stays the
framework pack's and becomes the shell; each module's remote is a package of its
own, `web/modules/<module>`, owned per module by the technique pack. The one
file whose content changes is the shell's page for a module, which mounts the
remote instead of holding the screens: `react-router` branches on the
architecture there (ADR-0006).
