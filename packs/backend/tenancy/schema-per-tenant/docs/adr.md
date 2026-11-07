# Tenancy: a schema per tenant

**Status:** Accepted (generated)

## Decision

All tenants share the database, and each tenant has a schema of its own per module: `<module>_<tenant>`, such as `billing_tenant_a`, with its own migrations history table. The tables carry no tenant column, and the domain model never sees a tenant. A module's DbContext works in the schema of the current tenant: EF Core caches one model per module and schema, and the migrations `dotnet ef` writes for the module's schema are moved to the tenant's when they run.

The tenants are a list, `Tenancy:Tenants`, which the AppHost sets on the MigrationRunner and on the host (`tenant-a` and `tenant-b` locally, the groups the identity provider's local users belong to). The MigrationRunner first migrates every module in its own schema, which checks each model against its snapshot, then creates and migrates every module's schema for each listed tenant. The host never creates a schema and never migrates: it needs no rights to change the database's structure, and several instances never race to migrate a tenant.

The current tenant comes from the user's token: {{ if project["backend.auth"] != "none" }}{{ tenant_claim_adr }}{{ else }}the identity provider's tenant claim, and `backend.auth` `none` signs nobody in{{ end }}. Work outside a request — consumers, jobs — sets the tenant explicitly with `TenantScope.Begin(tenantId)`. A tenant that is not listed, or no tenant at all, works in the module's own schema, which holds no rows: every query there finds nothing, and saving fails.

## Consequences

- A tenant's rows cannot be read by another tenant's query: they are in another schema, not behind a filter.
- Adding a tenant means adding it to `Tenancy:Tenants` in the AppHost and running the MigrationRunner again; a tenant the token names but the list does not sees nothing until then. A tenant name is lower-case letters and digits joined by hyphens; any other name in the list fails the runner and the host.
- Every migration runs once per tenant, so a migration's time grows with the number of tenants. Raw SQL in a migration (`migrationBuilder.Sql`) runs as written, in whatever schema it names, so it must not name the module's schema.
- Reading across tenants means opening each tenant's schema in turn; there is no query over all of them.
