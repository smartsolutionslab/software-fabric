## Tenancy: none

- The application has one tenant. Do not add a `TenantId` (property, field or event member) to a module; an architecture rule fails the build if one appears.
- Work that needs several tenants is a project decision: escalate it with `needs:decision` instead of starting it in one module.
