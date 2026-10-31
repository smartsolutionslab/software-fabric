## Tenancy: tenant column

- Never add a `TenantId` to domain classes; the persistence layer adds and filters it.
- Never call `IgnoreQueryFilters()` without a reason written in the PR.
- In consumers and background work, wrap the work in `using var tenant = TenantScope.Begin(message.TenantId);`. Events about one tenant's data carry its `TenantId`.
- Local users: `developer` is in `tenant-a`, `developer-b` in `tenant-b` (passwords equal the user names).
