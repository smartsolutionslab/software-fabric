{{~ reads_database = project["backend.database"] == "sqlserver" ? "SQL Server" : "PostgreSQL" ~}}
# Reads: Dapper in query handlers

**Status:** Accepted (generated)

## Decision

EF Core keeps the model, the writes and the migrations: commands change state through a module's `DbContext`, and `src/MigrationRunner` creates every table. Query handlers may read with Dapper instead. A query handler takes `IReadConnection` (`src/BuildingBlocks/Persistence`), opens a connection to the project's database and maps the rows of hand-written SQL straight into its result.

The connection comes from the database driver's own data source on Aspire's `database` connection string, one for the whole project, registered with every module's infrastructure. The database pack names the driver through the slot `read-connection.data-source`, so this pack holds no driver of its own.

SQL is written for the project's one database, {{ reads_database }}. There is no dialect layer and no query builder: a project has one database (ADR-0001 of the generator), and a query written for it can use everything it offers.

## Consequences

- An architecture rule fails the build when a module's Domain, Infrastructure or Api project references Dapper, or when a command handler takes `IReadConnection`: Dapper reads, it never writes.
- Dapper's SQL does not pass through EF Core's query filters. This option therefore requires `backend.tenancy: none`; a tenant column would leak every tenant's rows into every read. Row-level security in the database may lift that later, as its own decision.
- A query names tables and columns as the migrations create them. A renamed column breaks the SQL when it runs, not when it builds; a test of the query handler against the database (as `tests/Reads.Tests` does) catches it.
- A module reads only its own schema. Another module's data is still reached through its contract.
