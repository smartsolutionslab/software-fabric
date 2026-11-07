# Test framework: TUnit

**Status:** Accepted (generated)

## Decision

TUnit on Microsoft.Testing.Platform (`TUnit.Engine`): every test project is an executable, run with `dotnet test --project <test project>`. TUnit discovers tests with a source generator at build time, not by reflection at run time. Test names are sentences with underscores (`An_unpaid_invoice_can_be_cancelled`). `[Test]` marks a test; `[Arguments(...)]` adds one case of a table to a `[Test]` method.

The project references `TUnit.Engine`, not the `TUnit` package: that package adds TUnit's own assertions, which the chosen assertion library replaces, and Microsoft.Testing.Platform's telemetry. Only test projects reference it, since it makes a project a test executable; a program under `tests/` (`IsTestProject` false) keeps its own entry point. `TUnit.Core` is imported in test projects only; TUnit's other implicit using, `static TUnit.Core.HookType`, is switched off so that `Assembly`, `Class` and `Test` mean what they mean elsewhere.

The architecture rules in `tests/Architecture.Tests/Rules` run as one test with a case per rule (`[MethodDataSource]`). The tests this pack generates leave their assertions to the chosen assertion library, so they read like every other test. Each module gets a test project with a test that builds its service container with validation on, so a module that cannot resolve its own services fails.

TUnit makes **a new instance of a test class for each test**, so no test sees what another left in a field. A class whose work must be set up and torn down around each test implements `ITestLifetime` (`tests/TestRun/ITestLifetime.cs`, compiled into every test project): TUnit's `IAsyncInitializer`, whose `InitializeAsync` TUnit runs before the test, and `IAsyncDisposable`, whose `DisposeAsync` it runs after it.

TUnit registers the tests of every test assembly a test project loads, so a test project that references another (the evals' gate references `tests/Evals`) would run that project's tests as well. `tests/TestRun/OwnTestsOnlyAttribute.cs`, compiled into every test project, keeps each test to its own project: elsewhere it is reported skipped, because TUnit offers no way to leave a registered test out of a run.

### Run fixtures

Something expensive that every test of a project shares, such as the running application of the integration tests, is a **run fixture**: a class implementing `ITestLifetime`. A test class takes it as its constructor parameter and says so with `[ClassDataSource<T>(Shared = SharedType.PerTestSession)]` on the class. TUnit then makes one instance for the whole test session, initialises it before the first test that takes it and disposes it after the last. Nothing is named on the assembly: a fixture no class takes never starts.

### Coverage gate

CI runs the tests with code coverage (Microsoft.Testing.Extensions.CodeCoverage) through `.github/scripts/test.sh`, which then checks the line coverage of three layers against `tests/coverage.props`:

| Layer | Assemblies | Threshold (default) |
|---|---|---|
| Domain | each module's `<Module>.Domain` | 90 % |
| Application | each module's `<Module>.Application` | 80 % |
| Kernel | `src/Kernel` | 90 % |

The layer is the one the module project's name carries (`ModuleLayer` in `src/Modules/Directory.Build.props`), so a new module is gated without a list. Each assembly is checked on its own, so a well-tested module cannot hide an untested one; a line counts as covered when any test project runs it. An assembly with no executable lines has nothing to check, and code that source generators write (under `obj/`) is not counted: it is not this project's code to test. Infrastructure, Api and the host are left to the integration tests, which run the host in its own process and are not measured.

**To change a threshold, edit `tests/coverage.props`**; the gate reads it on every run. Lowering one is a decision for this project's ADRs, not a way to get a pull request through.

The gate belongs to the test framework, not to the test discipline: it applies with red-first and with test-after alike.

## Consequences

- CI runs each test project; a new module's tests run without editing the workflow.
- TUnit runs tests in parallel, those of one class included. A test that changes state another test reads must not depend on running alone; `[NotInParallel]` serialises the tests that truly cannot share.
- Untested Domain or Application code fails CI with the assembly, the layer and the number: `Shop.Modules.Billing.Domain: 61.5 % line coverage, below the Domain threshold of 90 % (tests/coverage.props).`
- The same check runs locally with `bash .github/scripts/test.sh` after `dotnet build build.proj -c Release`. Given test projects, it runs only those (`bash .github/scripts/test.sh tests/Modules/Billing.Tests/Billing.Tests.csproj`); the integration tests need Docker.
- Coverage says which lines ran, not that anything checked them: it is a floor under the tests, not proof of them.
