# Tenancy: none

**Status:** Accepted (generated)

## Decision

The application serves one tenant. Tables carry no tenant column, queries no tenant filter, and signed-in users belong to no tenant group. Keeping customers' data apart is not something a module does on its own.

## Consequences

- No module may grow a `TenantId` of its own; an architecture rule fails the build if one does. Half-built tenancy, a column some tables have and some queries filter, is worse than none.
- Serving several tenants later is a project-wide change (a tenancy option chosen with its own ADR and a data migration), not a column added module by module.
