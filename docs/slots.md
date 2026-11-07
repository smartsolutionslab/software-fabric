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
  TestRun/                      backend.test.framework.nunit: the run fixtures; tunit: ITestLifetime, OwnTestsOnlyAttribute; compiled into every test project
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
| `templates.functions` *(added with the kernel options)* | — (Scriban code, prefixed to every template) | backend.guards.* (`not_null`, `not_blank`); the test-framework and assertion packs (the test vocabulary below, M3-11); the integration pack (the integration-fixture vocabulary below, M3-12); the identity-provider and auth-client packs (the identity-provider vocabulary below, M3-13) |
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
| `tests.build-properties` *(added in M1-02)* | `tests/Directory.Build.props` | xunit (`OutputType Exe` for xUnit v3); nunit (`OutputType Exe` and `EnableNUnitRunner`); tunit (`OutputType Exe`, and `TUnitImplicitUsings` false) |
| `tests.packages` | `tests/Directory.Build.props` | xunit, nunit or tunit, shouldly or awesome-assertions, nsubstitute or moq, aspire-fixture; nunit and tunit also compile `tests/TestRun/*.cs` into every test project; tunit references `TUnit.Engine` from test projects only; awesome-assertions compiles `tests/Assertions/*.cs` into every project under `tests/` |
| `tests.usings` | `tests/Directory.Build.props` | xunit, nunit or tunit (test projects only: a program under `tests/` keeps `System.ComponentModel`'s `[Description]`), shouldly or awesome-assertions, nsubstitute or moq |
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
| `test_run_fixture type` | `[assembly: AssemblyFixture(typeof(type))]`: `type` starts once per test run, and test classes take it as a constructor parameter (*M3-12*) |
| `test_lifetime` | `IAsyncLifetime`, the interface of a class whose `test_setup` and `test_teardown` run around its tests, or once around the run for a fixture (*M3-12*) |
| `test_setup`, `test_teardown` | `public async ValueTask InitializeAsync()`, `public async ValueTask DisposeAsync()`; another framework may return several lines (an attribute above the method), which a template indents as Scriban does (*M3-12*) |
| `test_takes_run_fixture type` | nothing: the attributes, each ending its own line, of a test class whose constructor takes the run fixture `type`; xUnit injects it by itself, a framework that does not (NUnit, TUnit) names it on the class. A template writes it on its own line right above the class, as `{{~ … ~}}`, so that an empty result leaves no blank line (*#256*) |
| `actual \| should_be expected [message]` | `actual.ShouldBe(expected)`; also `should_be_ignoring_order` (sequences), `should_contain`, `should_not_contain`, `should_start_with`, `should_be_greater_than`, `should_all_be` (a predicate), `should_be_subset_of`, `should_be_same_as` |
| `actual \| should_be_true [message]` | `actual.ShouldBeTrue()`; also `should_be_false`, `should_be_null`, `should_not_be_null`, `should_be_empty`, `should_not_be_empty`, `should_be_in_order` |
| `actual \| should_be_in_range from to` | `actual.ShouldBeInRange(from, to)` |
| `actual \| should_have_single_item` | the single item |
| `actual \| should_be_of_type type`, `should_be_assignable_to type` | the value as `type` |
| `should_throw type action`, `should_throw_async type action` | the exception (awaitable for the async one) |
| `should_not_throw action` | `Should.NotThrow(action)` |

NUnit (`backend.test.framework.nunit`, *#256*) returns `[Test]` for both
test attributes (a table's method may carry `[Test]` beside its cases),
`[TestCase(arguments)]`, `TestContext.CurrentContext.CancellationToken`,
`TestContext.Out.WriteLine(text)`; a run fixture is
`[assembly: <namespace>.RunFixture(typeof(type))]`, and
`test_takes_run_fixture type` is `[RunFixture(typeof(type))]` on its own
line; `test_lifetime` is `ITestLifetime` (an `IAsyncDisposable`),
`test_setup` `[SetUp]` above `public async ValueTask InitializeAsync()`,
`test_teardown` `public async ValueTask DisposeAsync()`, which NUnit calls
when it disposes each test's instance.

TUnit (`backend.test.framework.tunit`, *#257*) returns `[Test]` for both
test attributes, `[Arguments(arguments)]`,
`TestContext.Current!.Execution.CancellationToken`,
`TestContext.Current?.Output.WriteLine(text)`. TUnit shares a run fixture
through the classes that take it, not the assembly:
`test_takes_run_fixture type` is
`[ClassDataSource<type>(Shared = SharedType.PerTestSession)]` on its own
line, and `test_run_fixture type` is a comment saying so. `test_lifetime`
is `ITestLifetime` (TUnit's `IAsyncInitializer` and `IAsyncDisposable`),
`test_setup` `public async Task InitializeAsync()`, which TUnit runs
before each test and once for a shared fixture, `test_teardown`
`public async ValueTask DisposeAsync()`.

Scriban indents every later line of a multi-line result by the
indentation of the line the call starts, when nothing but whitespace
precedes it there: write a multi-line argument's later lines that much
less indented than they should come out.

AwesomeAssertions (`backend.test.assertions.awesome-assertions`, *#258*)
returns `actual.Should().Be(expected)` and so on, a message as
`because: message`, `BeEquivalentTo` for `should_be_ignoring_order`,
`OnlyContain` for `should_all_be`, `BeInAscendingOrder` for
`should_be_in_order`, and `FluentActions.Invoking(action)` (or `Awaiting`,
which takes a lambda or a task) for the three `should_*throw*`. Its file
`tests/Assertions/AssertionExtensions.cs` adds what the vocabulary asks of
every value: `Be` on a sequence (AwesomeAssertions' `Equal`, the items in
order), and `.Value()`, the value an assertion returns (the single item, the
value as a type, the exception), as a method, because AwesomeAssertions'
`Which` is a property and does not stand as a statement.

Every assertion renders after its `actual`, so `actual` may continue the
line above (`` `.Message` ``) as in a fluent chain. A template writes
`{{ `total` | should_be `42` }};` for `total.ShouldBe(42);`. The fixtures
themselves are the integration pack's vocabulary, below; only their
lifetimes are this one's.

#### The integration-fixture vocabulary *(M3-12)*

A pack's generated tests never name the integration fixture either: how
a test reaches the running application, calls the host as an anonymous or
signed-in caller, or reaches the application's database alone comes from
`templates.functions` that every pack of `backend.test.integration`
defines, so Testcontainers (#260) plugs in by defining the same functions
instead of editing every pack's tests. The integration pack's fixture
starts through the test framework's `test_run_fixture` and `test_lifetime`,
so it does not refuse another framework. `IntegrationVocabularyTests` fails
when the pack defines more or fewer functions, when a class taking a fixture
is not preceded by that fixture's attributes, when a template outside the
pack's own files (its fixture, `HostTests.cs`, `Integration.Tests.csproj`)
names the Aspire fixture, or when a template refuses another integration
option or test framework with `#error`. The MCP client's tests read the
AppHost's model and start nothing; Aspire is the composition root whatever
the integration option, so they are not the fixture's.

| Function | Returns (aspire-fixture) |
|---|---|
| `integration_fixture` | `AspireFixture aspire`: the constructor parameter of a test class against the running application |
| `host_client` | `aspire.CreateHostClient()`: an `HttpClient` calling the host anonymously |
| `signed_in_host_client user` | `aspire.CreateHostClientAsync(user)`: awaitable, a client calling the host as the identity provider's local user `user` (a C# string) |
| `running_application` | `aspire.App`: the running Aspire application, which the identity provider's test-user functions take (*M3-13*) |
| `integration_fixture_attributes` | `test_takes_run_fixture "AspireFixture"`: written right above every class taking `integration_fixture` (*#256*) |
| `database_fixture` | `AspireDatabase database`: the constructor parameter of a test class against the application's database alone, migrated, with nothing else running |
| `database_fixture_attributes` | `test_takes_run_fixture "AspireDatabase"`: written right above every class taking `database_fixture` (*#256*) |
| `database_connection_string` | `database.ConnectionString` |
| `database_fixture_file namespace` | the C# file declaring that database fixture in `namespace` (rag's `tests/Rag.Tests/AspireDatabase.cs`) |
| `database_fixture_package_references`, `database_fixture_project_references` | the `PackageReference` and `ProjectReference` items a test project needs for the database fixture |

A database fixture of another option lands in rag's file `AspireDatabase.cs`
until rag's target is renamed, which would move the file in every rag
project.

#### The identity-provider vocabulary *(M3-13)*

A pack never names the identity provider either: where its roles sit in
the token, how a test user signs in, its login page and the shape of its
ID token come from `templates.functions` of the `backend.auth` pack, and
how the browser holds its access token from those of the
`frontend.auth-client` pack, so generic-oidc (#251), entra-id (#252) and
msal (#266) plug in by defining the same functions instead of editing
roles, claims, signalr, ag-ui, the micro-frontends, the MCP server and the
base's policy rule. `backend.auth` `none` defines none of them: a template
calls them only when `backend.auth` is not `none`, and a provider pack that
lacks one fails generation naming it. `IdentityVocabularyTests` fails when a
provider or auth-client pack defines more or fewer functions, when a
template refuses another provider or auth client with `#error` or
`throw new Error(...)`, or when a project rendered with stand-in
vocabularies names Keycloak (`keycloak`, `/realms/`, `realm_access`,
`admin-cli`, its login form) or oidc-client-ts's token storage outside their
own files, the composition root and the host's shared files. Comments are
prose and still describe Keycloak where it is the only provider. A
multi-line result called after other text on its line is indented by the
caller: `{{ accept_query_token ... | string.replace "\n" "\n        " }}`.

An identity provider pack also brings, as its own files and slot
contributions: the host's authentication (`host.services`,
`host.middleware`), `GET /me` answering `CurrentUser` (the frontend's
OpenAPI copy starts with it whenever `backend.auth` is not `none`), the
architecture tests' host references (`tests.architecture.references`), the
mapping of its token's roles to `ClaimTypes.Role` when
`backend.authorization` has `roles`, and the local test users the tests
sign in as, passwords equal to their names: `developer` (and `developer-b`
with tenancy), plus `visitor`, `clerk` and `analyst` as the roles and
claims ADRs describe.

| Function | Returns (keycloak) |
|---|---|
| `role_mapping_source` | `src/Host/KeycloakRoleClaims.cs`: the source that puts the token's roles into `ClaimTypes.Role`, which `OnlyModulePoliciesCheckRoles` exempts |
| `unstarted_host_settings` | `builder.UseSetting("Authentication:Authority", ...)`: what a host started with `WebApplicationFactory` and no provider reachable needs (`builder` an `IWebHostBuilder`) |
| `test_user_token app user` | statements, run in an async method, that leave in `token` the access token (`token.AccessToken`) of the local user `user` (a C# string); `app` is the running Aspire application (`running_application`, or the fixture's `App`) |
| `test_user_token_types` | the private types those statements need, members of the same class |
| `short_lived_test_user_token app user lifespan` | the same with a token that expires after `lifespan` seconds (a C# `int`), other users' tokens untouched; `cancellationToken` in scope |
| `short_lived_test_user_token_members app`, `short_lived_test_user_token_types` | the private methods and types those statements need |
| `short_lived_test_user_token_namespaces` | the namespaces they need, separated by spaces, for the caller's sorted `using`s |
| `accept_query_token services path` | an expression on `services` that makes the host accept a browser's token from the `access_token` query parameter on `path` (a C# string) only, where a WebSocket or EventSource cannot send a header |
| `accept_query_token_namespaces` | the namespaces that expression needs, separated by spaces |
| `browser_sign_in page user` | Playwright statements that sign the local user `user` (a TS string) in on the provider's login page |
| `browser_authority` | the provider's local authority URL (`http://localhost:8080/realms/<name>`), the browser's fallback when `VITE_OIDC_AUTHORITY` is unset |
| `id_token_roles roles` | the ID token property holding the roles `roles` (TS array items), for test tokens |
| `id_token_has_role profile role` | TS statements returning whether the ID token claims `profile` hold `role` |

| Function | Returns (oidc-client-ts) |
|---|---|
| `browser_access_token_import` | the TS import that `browser_access_token` needs |
| `browser_access_token` | `accessToken()`: the signed-in user's access token, or `undefined` |
| `stored_access_token page` | awaitable, in Playwright: the access token the app in `page` signed in with |
| `store_access_token_import`, `store_access_token token` | in a unit test, the import and the statement that store `token` (a TS string) as the signed-in user's access token, where `browser_access_token` finds it |

The integration-fixture vocabulary gains `running_application`
(`aspire.App`), the running application the provider's test users sign in
against. A test user's token for another integration option, and the
Mailpit gate Keycloak keeps in the integration pack's `HostTests.cs`, are
#260's and Keycloak's to move; the column tenancy pack still reads Keycloak's
group paths (`groups`, `/tenants/<tenant>`) from the token.

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
| `ci.backend-steps` | `.github/workflows/ci.yml` | xunit, nunit or tunit (runs every test project with code coverage, then the coverage gate; the integration tests need Docker, which GitHub's runners have) |
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
