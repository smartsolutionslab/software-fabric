# Tenancy: a tenant column

**Status:** Accepted (generated)

## Decision

All tenants share the database and its tables. Every entity a module's DbContext maps gets a `TenantId` column as an EF Core shadow property — the domain model never sees it. A global query filter limits every query to the current tenant; saving a new row stamps it with the current tenant, and saving without one fails.

The current tenant comes from the user's token: membership of exactly one group under `/tenants/` (Keycloak: `/tenants/tenant-a`). Work outside a request — consumers, jobs — sets the tenant explicitly with `TenantScope.Begin(tenantId)`.

## Consequences

- Forgetting a tenant filter is impossible; reading across tenants needs `IgnoreQueryFilters()`, visible in review.
- Integration events that concern one tenant carry its id, so consumers can open a `TenantScope`.
- A user in no tenant, or in several, sees nothing and cannot save.
