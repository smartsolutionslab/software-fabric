# Data access: EF Core, one DbContext per module

**Status:** Accepted (generated)

## Decision

Each module owns one `DbContext`, deriving from `ModuleDbContext`, in its Infrastructure project. All modules share one database, each in its own schema named after the module. Entity configurations are `IEntityTypeConfiguration<T>` classes in the same project and are applied automatically. Each context gets a health check.

## Consequences

- Modules never join across schemas; another module's data is reached through its contract, not its tables.
- Migrations are per module and per context.
