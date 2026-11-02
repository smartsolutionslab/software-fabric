## Test framework: xUnit v3

- Tests live in `tests/Modules/<Module>.Tests/` for a module, `tests/Architecture.Tests/` for rules, `tests/Kernel/` for the Kernel. Name tests as sentences with underscores.
- Run one project with `dotnet test --project tests/Modules/<Module>.Tests/<Module>.Tests.csproj`; build first with `dotnet build build.proj`.
- `[Fact]` for one case, `[Theory]` with `[InlineData]` for a table of cases.
- CI fails when a module's Domain or Application, or the Kernel, is below the line coverage `tests/coverage.props` sets for its layer (90 %, 80 % and 90 % as generated). Check before pushing with `bash .github/scripts/test.sh` after a Release build. Add tests; never lower a threshold to get to green.
