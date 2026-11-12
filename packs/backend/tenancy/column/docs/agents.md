{{~ across = cross_tenant_reads ~}}
## Tenancy: tenant column

- Never add a `TenantId` to domain classes; the persistence layer adds and filters it.
{{~ if across ~}}
- To let staff read every tenant's rows, map the endpoint with `.RequireAuthorization(<Module>PolicyNames.ReadAllTenants).AcrossTenants()` and read in its handler; never call `IgnoreQueryFilters()` for it. Such an endpoint writes nothing: saving or publishing inside it fails. The rule `AcrossTenantsRequiresTheCrossTenantRole` fails the build for one whose policy does not require `CrossTenantRole.Name`.
- Never call `IgnoreQueryFilters()` without a reason written in the PR.
{{~ else ~}}
- Never call `IgnoreQueryFilters()` without a reason written in the PR.
{{~ end ~}}
- In consumers and background work, wrap the work in `using var tenant = TenantScope.Begin(message.Metadata.TenantId);`. Integration events carry the tenant that published them in `message.Metadata.TenantId`, filled by the unit of work.
{{~ if across ~}}
- Local users: `developer` is in `tenant-a`, `developer-b` in `tenant-b`, and `{{ cross_tenant_role }}` / `{{ cross_tenant_role }}` holds the role `{{ cross_tenant_role }}` and is in no tenant (passwords equal the user names).
{{~ else ~}}
- Local users: `developer` is in `tenant-a`, `developer-b` in `tenant-b` (passwords equal the user names).
{{~ end ~}}
