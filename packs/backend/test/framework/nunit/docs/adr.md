# Test framework: NUnit

**Status:** Accepted (generated)

## Decision

NUnit on Microsoft.Testing.Platform (the NUnit runner, `EnableNUnitRunner`): every test project is an executable, run with `dotnet test --project <test project>`. Test names are sentences with underscores (`An_unpaid_invoice_can_be_cancelled`). `[Test]` marks a test; `[TestCase(...)]` adds one case of a table, and a method may carry `[Test]` beside its cases.

The architecture rules in `tests/Architecture.Tests/Rules` run as one test with a case per rule (`[TestCaseSource]`). The tests this pack generates leave their assertions to the chosen assertion library, so they read like every other test. Each module gets a test project with a test that builds its service container with validation on, so a module that cannot resolve its own services fails.

NUnit makes **a new instance of a test class for each test** here (`FixtureLifeCycle(InstancePerTestCase)`, set once in `tests/TestRun/RunFixtures.cs`), so no test sees what another left in a field. A class whose work must be set up and torn down around each test implements `ITestLifetime`: `[SetUp]` on `InitializeAsync`, and `DisposeAsync`, which NUnit calls when it disposes the test's instance after the test.

### Run fixtures

Something expensive that every test of a project shares, such as the running application of the integration tests, is a **run fixture**: a class implementing `ITestLifetime`, named once on the assembly with `[assembly: RunFixture(typeof(...))]`. `RunFixtures`, a setup fixture over the whole test assembly, starts each before the first test and disposes them after the last. A test class takes the one instance as its constructor parameter and says so with `[RunFixture(typeof(...))]` on the class: NUnit does not hand a constructor anything by itself. A test class takes at most one run fixture. The three types live in `tests/TestRun/` and are compiled into every test project through `tests/Directory.Build.props`.

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
- Tests run one after another, as NUnit runs them unless told otherwise; a slow project is slower than under a framework that runs classes in parallel.
- Untested Domain or Application code fails CI with the assembly, the layer and the number: `Shop.Modules.Billing.Domain: 61.5 % line coverage, below the Domain threshold of 90 % (tests/coverage.props).`
- The same check runs locally with `bash .github/scripts/test.sh` after `dotnet build build.proj -c Release`. Given test projects, it runs only those (`bash .github/scripts/test.sh tests/Modules/Billing.Tests/Billing.Tests.csproj`); the integration tests need Docker.
- Coverage says which lines ran, not that anything checked them: it is a floor under the tests, not proof of them.
