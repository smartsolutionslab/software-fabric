{{~ across = cross_tenant_reads ~}}
## Tenancy: tenant column

- Never add a `TenantId` to domain classes; the persistence layer adds and filters it.
{{~ if across ~}}
- To let staff read every tenant's rows, map the endpoint with `.RequireAuthorization(<Module>PolicyNames.ReadAllTenants).AcrossTenants()` and read in its handler; never call `IgnoreQueryFilters()` for it. Such an endpoint writes nothing: saving, publishing, `ExecuteUpdate`, `ExecuteDelete` or `ExecuteSql` inside it fails. SQL you write yourself is never filtered by tenant, here or anywhere: never write with `SqlQuery` or `FromSql`. The rule `AcrossTenantsRequiresTheCrossTenantRole` fails the build for one whose policy does not require `CrossTenantRole.Name`.
- Records every tenant reads, which the operator's staff write (a knowledge base), are an entity type shared as a whole: call `builder.SharedByAllTenants()` in its configuration, on the root of its hierarchy. It has no tenant column and no filter, so every tenant reads it; the model build fails if it references a tenant's type, or if a tenant's type references it with a cascading delete (give such a reference `OnDelete(DeleteBehavior.Restrict)`). Write it only on an endpoint mapped with `.RequireAuthorization(<Module>PolicyNames.WriteSharedRecords).WritesSharedRecords()`; anywhere else saving it, `ExecuteUpdate` or `ExecuteDelete` on it fails, and inside that endpoint saving anything else fails. Never map one endpoint with both `.WritesSharedRecords()` and `.AcrossTenants()`; the rule `WritesSharedRecordsRequiresTheCrossTenantRole` fails the build for that and for a policy that does not require `CrossTenantRole.Name`. Whether a customer may see a given record (published or not) is your query's and policy's rule.
- To let staff write into one tenant's rows (a new row, or one of its existing rows), map the endpoint on a route that names the tenant with `.RequireAuthorization(<Module>PolicyNames.WriteForTenants).ForTenant()`: `endpoints.MapPost("/contracts/{tenant}", ...)`. Its handler then works in that tenant as the tenant's own users do; keep the tenant out of the command. A tenant missing from `Tenancy:Tenants` answers 404 `tenant-not-found`; add a new tenant there (`src/AppHost/AppHost.cs`, `string[] tenants`) and to the identity provider's tenant groups. `GET /tenants` lists them for staff. Never map one endpoint with `.ForTenant()` and `.AcrossTenants()` or `.WritesSharedRecords()`; the rule `ForTenantRequiresTheCrossTenantRole` fails the build for that and for a policy that does not require `CrossTenantRole.Name`.
- Never call `TenantScope.Begin` in an Api project or a command or query handler, not even inside `AcrossTenants()`: the rule `TenantScopeBeginsOnlyOutsideRequests` fails the build. Request code gets its tenant from the token or from `ForTenant()`.
- Never call `IgnoreQueryFilters()` without a reason written in the PR.
- In consumers and background work, wrap the work in `using var tenant = TenantScope.Begin(message.Metadata);`. Integration events carry the tenant that published them in `message.Metadata.TenantId`, filled by the unit of work; an event published inside `WritesSharedRecords()` carries none, and that scope then has no tenant: it reads shared records and saves nothing.
{{~ else ~}}
- Never call `IgnoreQueryFilters()` without a reason written in the PR.
- In consumers and background work, wrap the work in `using var tenant = TenantScope.Begin(message.Metadata.TenantId);`. Integration events carry the tenant that published them in `message.Metadata.TenantId`, filled by the unit of work.
{{~ end ~}}
{{~ if across ~}}
- Local users: `developer` is in `tenant-a`, `developer-b` in `tenant-b`, and `{{ cross_tenant_role }}` / `{{ cross_tenant_role }}` holds the role `{{ cross_tenant_role }}` and is in no tenant (passwords equal the user names).
{{~ else ~}}
- Local users: `developer` is in `tenant-a`, `developer-b` in `tenant-b` (passwords equal the user names).
{{~ end ~}}
