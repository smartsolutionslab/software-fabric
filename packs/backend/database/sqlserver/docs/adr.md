# Database: SQL Server

**Status:** Accepted (generated)

## Decision

SQL Server, run by Aspire as a container with a data volume in development (`sqlserver` resource, `database` database). The host receives the connection string through Aspire's reference; EF Core uses its SQL Server provider (`Microsoft.EntityFrameworkCore.SqlServer`).

## Consequences

- Each module keeps its tables and its migrations history in a schema of its own, as with any database here; `MigrateAsync` creates the database and the schema when they are missing.
- EF Core's retrying execution strategy (`EnableRetryOnFailure`) stays off: units of work open their own transactions, which a retrying strategy refuses. A transient failure surfaces as an error, as it does on PostgreSQL.
- Raw SQL that packs keep in the database (an outbox, Wolverine's envelopes) is written in T-SQL for this choice; use SQL Server features where they earn their keep, since this project does not pretend to be database-neutral.
- The development image accepts the SQL Server licence (`ACCEPT_EULA`) for local use; a production deployment needs its own licence.
