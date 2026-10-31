## Test framework: xUnit v3

- Tests live in `tests/Modules/<Module>.Tests/` for a module, `tests/Architecture.Tests/` for rules. Name tests as sentences with underscores.
- Run one project with `dotnet test --project tests/Modules/<Module>.Tests/<Module>.Tests.csproj`; build first with `dotnet build build.proj`.
- `[Fact]` for one case, `[Theory]` with `[InlineData]` for a table of cases.
