# Reads: EF Core

**Status:** Accepted (generated)

## Decision

Query handlers read through EF Core, as commands write through it: the module's `DbContext`, its model and its mappings are the only way to its tables. There is no second data-access library for reads, and no module references Dapper.

## Consequences

- An architecture rule fails the build when a module references Dapper.
- Hand-written SQL for reads is a project-wide choice (`backend.data-access.reads: dapper`, with its own ADR), which brings a read connection for the project's database and the rule that only query handlers read with it. A package added to one module by hand skips both.
