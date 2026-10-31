# Database: PostgreSQL

**Status:** Accepted (generated)

## Decision

PostgreSQL, run by Aspire as a container with a data volume in development (`postgres` resource, `database` database). The host receives the connection string through Aspire's reference; EF Core uses Npgsql.

## Consequences

- Use PostgreSQL features freely (jsonb, arrays, citext) where they earn their keep; this project does not pretend to be database-neutral.
