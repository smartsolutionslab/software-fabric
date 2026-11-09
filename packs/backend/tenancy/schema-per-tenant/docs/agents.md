## Tenancy: a schema per tenant

- Never add a `TenantId` to a module's types or tables; each tenant's rows live in its own schema, `<module>_<tenant>`. An architecture rule fails the build if a module's type has one.
- The tenants are `Tenancy:Tenants`, set in `src/AppHost/AppHost.cs` (`string[] tenants`). To add one, add it there and run the MigrationRunner again; never create a tenant's schema from the host.
- Migrations are written as usual, with `dotnet ef` for the module's schema; the MigrationRunner moves them to each tenant's. Never name the module's schema in `migrationBuilder.Sql(...)`: raw SQL is not moved.
- Do not use `HasData`, entity splitting (`SplitToTable`, `SplitToView`), sequences (`HasSequence`, `UseHiLo`), or SQL or a provider annotation naming the module's schema (`billing.x`, `"billing"."x"`, `HasPostgresEnum("billing", ...)`): a module that does fails when its migration is made, and the MigrationRunner fails before any module migrates.
- In consumers and background work, wrap the work in `using var tenant = TenantScope.Begin(message.Metadata.TenantId);` before the first query or save; an injected DbContext is fine, as it picks its schema at its first use. A context first used without a listed tenant reads nothing and cannot save, even after the scope begins.
- Local users: `developer` is in `tenant-a`, `developer-b` in `tenant-b` (passwords equal the user names).
