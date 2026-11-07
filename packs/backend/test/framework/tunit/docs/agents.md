## Test framework: TUnit

- Tests live in `tests/Modules/<Module>.Tests/` for a module, `tests/Architecture.Tests/` for rules, `tests/Kernel/` for the Kernel. Name tests as sentences with underscores.
- Run one project with `dotnet test --project tests/Modules/<Module>.Tests/<Module>.Tests.csproj`; build first with `dotnet build build.proj`.
- `[Test]` for a test; add `[Arguments(...)]` for each row of a table. Assert with the assertion library; TUnit's own assertions are not referenced.
- A test runs only in its own project: one referenced from another test project is reported skipped there.
- Tests run in parallel, a class's own included: never let one test depend on state another leaves behind.
- Each test gets a new instance of its class. Setup and teardown around each test: implement `ITestLifetime`, `InitializeAsync` before the test and `DisposeAsync` after it.
- A class that needs the integration tests' running application takes it as a constructor parameter and carries `[ClassDataSource<...>(Shared = SharedType.PerTestSession)]`; never start the application in a test.
- CI fails when a module's Domain or Application, or the Kernel, is below the line coverage `tests/coverage.props` sets for its layer (90 %, 80 % and 90 % as generated). Check before pushing with `bash .github/scripts/test.sh` after a Release build. Add tests; never lower a threshold to get to green.
