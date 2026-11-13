{{~ claims = project["backend.authorization"] | array.contains "claims" ~}}
# Authorization: by role

**Status:** Accepted (generated)

## Decision

Being signed in is not enough for every endpoint: access can depend on the user's roles in the identity provider. Roles are checked in exactly one place, a module's authorization policies.

**A module registers its own policies.** Its Api project has one `IModulePolicies` (`<Module>Policies`), found by assembly scan like `IModule`, which adds policies under the names its Application project declares in `<Module>PolicyNames`, `<module>.<permission>` (`billing.approve-invoices`), each requiring one or more roles with `RequireRole`. `AddModuleInfrastructure` registers them. An endpoint names a policy with `RequireAuthorization(BillingPolicyNames.ApproveInvoices)`; handlers never check roles themselves.

{{ role_mapping_adr }} The roles live in the identity provider, not in the application: there is no role table and no role-management screen.

**Sample role and users** (development only, in {{ local_users_setup }}): the {{ role_kind }} `manager`, held by `developer` (and `developer-b` when the project has tenancy), and the user `visitor` / `visitor` with no roles (in `tenant-a` with tenancy, so a 403 is about the role, not the tenant). `fabric add-module` gives every new module the sample policy `<module>.manage`, which requires `manager`{{ if claims }} (and, with claims, `department=finance`: see the claims ADR){{ end }}, and the endpoint `GET /<module>/permissions/manage` that requires it: 200 with a `PermissionDto` naming the permission when the caller holds the policy, 403 when not. It is an ordinary endpoint (#609): it sends the module's `CanManage` query through the dispatcher, the OpenAPI document describes it, the module's page asks it through the generated client and shows "Manage" only on a 200, hides it on a 403, and shows any other failure (a 500, a host never reached) as an alert, and the integration tests prove the policy on it. `fabric add-module` puts a new module's probe into the frontend's copy of the document (the generator's ADR-0005). The role, the users and the sample policy were decided by the repository owner (2026-11-03); the endpoint is this pack's.

**The identity provider maps its roles.** Where the roles sit in the token is the identity provider's business: its pack puts them into `ClaimTypes.Role` (with {{ role_mapping_provider }}, `{{ role_mapping_source | string.split "/" | array.last | string.replace ".cs" "" }}` above) and names that file for `OnlyModulePoliciesCheckRoles`. This pack only checks them, so it works with every provider the generator offers.

## Gates

- `EveryEndpointPolicyIsRegisteredByItsModule` starts the host in Development and in Production, as `EveryEndpointDeclaresItsAuthorization` does, and fails for a module endpoint whose policy that module's `IModulePolicies` does not register, or which another module registers too (the host has one policy table; the last registration would win). An endpoint belongs to the module whose Api assembly declares its handler.
- `EveryPolicyIsRegisteredByOneModule` fails for a policy name two modules register, naming both, whether or not an endpoint or an MCP tool names it: whichever registration came last would silently decide what everything naming it requires. The host refuses to start on the same names, because both ask `ModulePolicyOwners` in BuildingBlocks.
- `OnlyModulePoliciesCheckRoles` parses every source under `src/` and fails, naming file and line, for `IsInRole`, `ClaimTypes.Role`, `[Authorize(Roles = ...)]` anywhere, and `RequireRole` outside a type implementing `IModulePolicies`. The identity provider's role mapping (`{{ role_mapping_source }}` with {{ role_mapping_provider }}) is exempt: it is where the roles enter.
- The integration tests (`<Module>RoleTests`) give `developer` 200, `visitor` 403 and an anonymous caller 401 on each module's sample endpoint, against the running stack.

## What the gates cannot see

- A role read by its claim type's string (`"http://schemas.microsoft.com/ws/2008/06/identity/claims/role"`, `"{{ id_token_roles_claim | string.split "." | array.first }}"`) or through reflection; a role check in code compiled only under an `#if` the parse leaves disabled.
- `new AuthorizeAttribute { Roles = ... }` written as an object initializer rather than an attribute.
- Whether a policy is the right one for its endpoint, or strong enough: review's to catch.
- An endpoint mapped outside both Development and Production, or one whose handler is declared outside its module's Api assembly (it is then nobody's, and only `EveryEndpointDeclaresItsAuthorization` checks it).
- A policy registered under a name without the `<module>.` prefix, as long as no other module registers the same name.
- A policy added without the builder's `AddPolicy` (through `policies.Services.Configure<AuthorizationOptions>(...)`, say): `ModulePolicyOwners` learns names only from `AddPolicy`, so it cannot see a name registered that way.

## Consequences

- A permission is one line in the module's `<Module>Policies` and a constant in its `<Module>PolicyNames`; changing who holds it is a change in the identity provider, not in code.
- A new module's `/<module>/permissions/manage` is in the OpenAPI document; add-module adds it to the frontend's copy, so the copy stays current and the page has its hook at once.
- {{ roles_production_adr }}
