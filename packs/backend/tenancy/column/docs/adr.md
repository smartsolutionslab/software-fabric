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

## Gates

- `AcrossTenantsRequiresTheCrossTenantRole` reads the host's endpoint table in Development and in Production and fails, naming the endpoint, for one mapped with `AcrossTenants()` whose authorization, its group's included, does not require `CrossTenantRole.Name` alone, or which allows anonymous callers.
{{~ end ~}}

## Consequences

{{~ if across ~}}
- Forgetting a tenant filter is impossible; reading across tenants is an endpoint that says so with `AcrossTenants()` and a policy requiring `{{ cross_tenant_role }}`, visible in review and checked by the rule. Handlers never call `IgnoreQueryFilters()` for it.
- An endpoint mapped with `AcrossTenants()` must do its reading in its handler: a result that queries lazily (an `IQueryable` or an `IAsyncEnumerable` serialized after the handler returns) runs outside the scope and finds nothing.
{{~ else ~}}
- Forgetting a tenant filter is impossible; reading across tenants needs `IgnoreQueryFilters()`, visible in review.
{{~ end ~}}
{{~ if entra_roles ~}}
- `AcrossTenants()` is not generated with Entra ID yet (the repository owner's decision, 2026-11-12): the host accepts a token only from a directory listed as a tenant, so there is no staff user in no tenant to grant it to. Letting staff sign in from a directory that is not a tenant is a production configuration decision of its own.
{{~ end ~}}
- Integration events that concern one tenant carry its id, so consumers can open a `TenantScope`.
- A user in no tenant, or in several, sees nothing and cannot save.
