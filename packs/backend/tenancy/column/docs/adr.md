{{~ across = cross_tenant_reads ~}}
{{~ entra_roles = project["backend.auth"] == "entra-id" && object.has_key(project, "backend.authorization") && array.contains(project["backend.authorization"], "roles") ~}}
# Tenancy: a tenant column

**Status:** Accepted (generated)

## Decision

All tenants share the database and its tables. Every entity a module's DbContext maps gets a `TenantId` column as an EF Core shadow property — the domain model never sees it. A global query filter limits every query to the current tenant; saving a new row stamps it with the current tenant, and saving without one fails.

The current tenant comes from the user's token: {{ tenant_claim_adr }}. Work outside a request — consumers, jobs — sets the tenant explicitly with `TenantScope.Begin(tenantId)`.
{{~ if across ~}}

**Staff read across tenants on endpoints that opt in** (the repository owner's decision, 2026-11-12). An endpoint mapped with `.RequireAuthorization(<Module>PolicyNames.ReadAllTenants).AcrossTenants()` runs its handler inside `TenantScope.AllTenants()`: the query filter becomes "every tenant, or this row's tenant", so the handler reads every tenant's rows without `IgnoreQueryFilters()`. `AcrossTenants()` adds an endpoint filter, which runs after authorization, and is the only way to open that scope. Each module's `<Module>PolicyNames.ReadAllTenants` (`<module>.read-all-tenants`) requires the role named once, in `CrossTenantRole.Name` (`{{ cross_tenant_role }}`), which the identity provider grants to staff who belong to no tenant; a user in a tenant is refused the endpoint (403), and staff see nothing on an endpoint without `AcrossTenants()`.

**Writes still need one tenant.** Inside the scope there is no current tenant: saving any change fails, as does publishing an integration event, as both do without a tenant. So does every command that is not a query (`ExecuteUpdate`, `ExecuteDelete`, `ExecuteSql`), which bypasses `SaveChanges` but not the query filter: a module context's interceptor, `AllTenantsWriteNothing`, refuses it. SQL a module writes itself and reads rows from (`SqlQuery`, `FromSql`) runs as written: no tenant filter applies to it, inside `AcrossTenants()` or outside it, so such SQL must name its tenant and never write. A `TenantScope.Begin(tenantId)` inside it narrows the work to that tenant again.

**Records shared by all tenants** (the repository owner's decisions on #623, 2026-11-12). One base the operator owns, such as a knowledge base its staff write and every customer reads, is an entity type shared as a whole, declared in its configuration with `builder.SharedByAllTenants()` (`src/BuildingBlocks/Tenancy/Wiring/SharedRecordsModel.cs`); the domain class stays as it is. Such a type gets no `TenantId` column and no tenant filter: every tenant's users, staff, and work without a tenant read its records, inside `AcrossTenants()` too. Which of them a customer may see (published or draft) is the module's rule, in its query and its endpoint's policy, not tenancy's. Declare it on the root of a hierarchy; owned types follow their owner. Building the model (`dotnet ef`, the MigrationRunner, the host) fails, naming both types, for a declaration below the root or on an owned type, and for a shared type that references a tenant's type by a foreign key or a navigation: a record every tenant reads would point at one tenant's. A tenant's record may reference a shared one, but never with a delete that cascades or sets null in the database (`Cascade`, `ClientCascade`, `SetNull`, EF Core's default for a required reference): deleting a shared record would change every tenant's rows, none of them loaded. The model build fails for one; configure `OnDelete(DeleteBehavior.Restrict)`, `NoAction` or `ClientSetNull`.

Only staff write them, on endpoints that opt in: `.RequireAuthorization(<Module>PolicyNames.WriteSharedRecords).WritesSharedRecords()` runs the handler inside `TenantScope.SharedRecords()`, which only that endpoint filter opens, after authorization. Each module's `<Module>PolicyNames.WriteSharedRecords` (`<module>.write-shared-records`) requires `CrossTenantRole.Name`, as `ReadAllTenants` does. Inside the scope there is no tenant: changes to shared types save, and a change to any tenant's type fails, as tenant rows are invisible there and a new one would have no tenant. Outside it, saving an added, changed or deleted shared record fails everywhere: a tenant's request, `AcrossTenants()`, a consumer, a job. So do `ExecuteUpdate` and `ExecuteDelete` on a shared type's table, which bypass `SaveChanges` and which no tenant filter narrows: a module context's interceptor, `SharedRecordsBulkWriteGuard`, refuses them, finding the table by the name the provider renders for it. SQL a module writes itself follows the rule above: nothing filters or guards it. One endpoint is never mapped with both `WritesSharedRecords()` and `AcrossTenants()`: the one exists to write, the other writes nothing.

An integration event published inside `WritesSharedRecords()` carries no tenant: `EventMetadata.TenantId` is a `string?` in these projects, null for such an event, which concerns every tenant. Everywhere else, publishing without a tenant still fails. A consumer begins `TenantScope.Begin(message.Metadata)`: the event's tenant, or with none, no tenant at all, which reads shared records, finds no tenant's rows and saves nothing. Passing `message.Metadata.TenantId` to `Begin(string)` does not compile here (a nullable warning, an error in Release), so no consumer mistakes a shared event for a tenant's. Indexing a shared record therefore happens inside the staff request, in its handler or a domain-event reaction within it, not in a consumer.

With `backend.tenancy: none` every record is shared already, so none of this is generated there; schema-per-tenant tenancy says in its ADR why it has none.

## Gates

- `AcrossTenantsRequiresTheCrossTenantRole` reads the host's endpoint table in Development and in Production and fails, naming the endpoint, for one mapped with `AcrossTenants()` whose authorization, its group's included, does not require `CrossTenantRole.Name` alone, or which allows anonymous callers.
- `WritesSharedRecordsRequiresTheCrossTenantRole` does the same for an endpoint mapped with `WritesSharedRecords()`, and fails one also mapped with `AcrossTenants()`.
- Building a module's model fails for a misplaced `SharedByAllTenants()`, for a shared type that references a tenant's type, and for a tenant's reference to a shared type that cascades on delete.
{{~ end ~}}

## Consequences

{{~ if across ~}}
- Forgetting a tenant filter is impossible; reading across tenants is an endpoint that says so with `AcrossTenants()` and a policy requiring `{{ cross_tenant_role }}`, visible in review and checked by the rule. Handlers never call `IgnoreQueryFilters()` for it.
- An endpoint mapped with `AcrossTenants()` must do its reading in its handler: a result that queries lazily (an `IQueryable` or an `IAsyncEnumerable` serialized after the handler returns) runs outside the scope and finds nothing.
- Writing the records every tenant reads is an endpoint that says so with `WritesSharedRecords()` and a policy requiring `{{ cross_tenant_role }}`; a tenant's code cannot change them by accident, in bulk or one by one. Shared records are visible to every tenant's users, so what staff paste into one reaches all of them.
{{~ else ~}}
- Forgetting a tenant filter is impossible; reading across tenants needs `IgnoreQueryFilters()`, visible in review.
{{~ end ~}}
{{~ if entra_roles ~}}
- `AcrossTenants()` is not generated with Entra ID yet (the repository owner's decision, 2026-11-12): the host accepts a token only from a directory listed as a tenant, so there is no staff user in no tenant to grant it to. Letting staff sign in from a directory that is not a tenant is a production configuration decision of its own. For the same reason `WritesSharedRecords()` is not generated with Entra ID: records shared by all tenants need staff to write them (#614).
{{~ end ~}}
- Integration events that concern one tenant carry its id, so consumers can open a `TenantScope`.
- A user in no tenant, or in several, sees nothing and cannot save.
