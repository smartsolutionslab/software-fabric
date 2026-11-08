{{~ reads_database = project["backend.database"] == "sqlserver" ? "SQL Server" : "PostgreSQL" ~}}
## Reads: Dapper

- Commands change state through EF Core, and migrations stay EF Core's. Only query handlers read with Dapper: take `IReadConnection` in the handler's constructor, `await using var connection = await reads.OpenAsync(cancellationToken);`, then `connection.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))`.
- Write SQL for {{ reads_database }}, the project's one database. Do not write SQL meant to run on another database.
- Pass every value as a parameter (`@name`, from an anonymous object); never put input into the SQL text.
- Read only your module's own schema (`<module>.<table>`), never another module's tables.
- Name the columns you read; alias them to the result's members where the names differ. No `SELECT *`.
- Only a module's Application project references Dapper, and a command handler never takes `IReadConnection`; an architecture rule fails the build otherwise.
- Dapper bypasses EF Core's query filters, so this project has no tenancy; adding tenancy needs another decision first (escalate with `needs:decision`).
- Test a query handler against the AppHost's database, as `tests/Reads.Tests` does, so a renamed column fails a test rather than a request.
