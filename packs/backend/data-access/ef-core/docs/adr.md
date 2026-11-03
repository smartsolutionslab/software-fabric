# Data access: EF Core, one DbContext per module

**Status:** Accepted (generated)

## Decision

Each module owns one `DbContext`, deriving from `ModuleDbContext`, in its Infrastructure project. All modules share one database, each in its own schema named after the module. Entity configurations are `IEntityTypeConfiguration<T>` classes in the same project and are applied automatically. Each context gets a health check.

A write a unique index refuses answers `409` problem details with the code `already-exists` instead of `500`. The host's exception handler for it (`UniqueConstraintExceptionHandler`, nested in `src/BuildingBlocks/Persistence/Wiring/UniqueConstraintResponses.cs`) recognises the database's own duplicate-key error, not an exception type:
{{~ if project["backend.database"] == "postgres" ~}}
PostgreSQL's SQLSTATE `23505` (`unique_violation`), on a `PostgresException` or on the `DbUpdateException` that wraps one.
{{~ else if project["backend.database"] == "sqlserver" ~}}
SQL Server's error numbers `2601` (duplicate key in a unique index) and `2627` (in a unique or primary key constraint), on a `SqlException` or on the `DbUpdateException` that wraps one.
{{~ end ~}}
Every other database failure, a lost update (`DbUpdateConcurrencyException`) included, stays `500`. The response names neither the index nor the colliding values.

This is a backstop for a race, not a way to report a duplicate. An application-level check stays the primary signal: a handler looks for the existing name or key first and returns a `Result` failure with its own code (`billing.invoice-number-taken`) that tells the caller which rule it broke. Two writers can both pass that check before either inserts; the unique index then refuses the second insert, and the caller gets a `409` rather than a server fault. Keep both the check and the index.

The host's `appsettings.json` sets `Microsoft.EntityFrameworkCore.Database.Command` to `Warning`. EF Core writes each executed command, with its SQL text (and its parameter values when sensitive data logging is on), at Information, so a host logging at Information would otherwise log every statement it runs. The architecture rule `SqlCommandsAreNotLoggedAtInformation` binds every `appsettings*.json` under `src` (the AppHost's aside: it runs no DbContext) into a real `LoggerFactory` and asks whether that category is enabled at Information, for all providers and for each provider the file gives its own levels; a misspelled category, or a louder level under a quiet `Default`, fails it as surely as a louder `Default`.

Each file is judged on its own, not merged over the `appsettings.json` it is layered on. A project's `appsettings.json` is always checked: with no level set at all, logging defaults to Information. An environment file (`appsettings.<Environment>.json`) is checked when it sets a `Default`, a prefix of the category or a wildcard. Those are the only settings that can change the category's level. A file that sets one of them must quiet the category itself, even when the base already does. That is stricter than the host, which merges the files, but the fix is one line. Each file then stays safe whatever it is layered on, and a reader can see the category's level in any one file without working out the merge.

## Consequences

- Modules never join across schemas; another module's data is reached through its contract, not its tables.
- Migrations are per module, in the module's Infrastructure project, with the history table in the module's schema. `src/MigrationRunner` applies them all and exits; Aspire starts the host only after it completes. Each module has a design-time factory so `dotnet ef` needs neither the application nor a database.
- The `409` is generic: the handler knows that a unique index refused the write, not which rule of the domain that index guards. A caller that needs to know gets it from the application-level check.
