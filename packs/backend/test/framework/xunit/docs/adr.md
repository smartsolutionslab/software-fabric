# Test framework: xUnit v3

**Status:** Accepted (generated)

## Decision

xUnit v3 on Microsoft.Testing.Platform: every test project is an executable, run with `dotnet test --project <test project>`. Test names are sentences with underscores (`An_unpaid_invoice_can_be_cancelled`).

The architecture rules in `tests/Architecture.Tests/Rules` run as one theory, one case per rule. The tests this pack generates leave their assertions to the chosen assertion library (slots `tests.assert-no-violations`, `tests.assert-does-not-throw`), so they read like every other test. Each module gets a test project with a test that builds its service container with validation on, so a module that cannot resolve its own services fails.

## Consequences

- CI runs each test project; a new module's tests run without editing the workflow.
