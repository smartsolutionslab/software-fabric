{{~ roles = project["backend.authorization"] | array.contains "roles" ~}}
# Authorization: by claim

**Status:** Accepted (generated)

## Decision

Being signed in is not enough for every endpoint: access can depend on information in the user's profile, carried as a claim in the token (a department, a verified e-mail address, a custom attribute). Claims are checked in exactly one place, a module's authorization policies.

**A module registers its own policies.** Its Api project has one `IModulePolicies` (`<Module>Policies`), found by assembly scan like `IModule`, which adds policies under the names its Application project declares in `<Module>PolicyNames`, `<module>.<permission>` (`billing.approve-invoices`), each requiring a claim, or a claim with given values, with `RequireClaim("department", "finance")`. `AddModuleInfrastructure` registers them. An endpoint names a policy with `RequireAuthorization(BillingPolicyNames.ApproveInvoices)`; handlers never read claims to decide access.
{{~ if roles ~}}

**Roles and claims together.** One policy may require a role and a claim: `policy.RequireRole("approver").RequireClaim("department", "finance")` means "approver in finance". Every requirement must hold; a user with only one of them gets 403.
{{~ end ~}}

**Claims come from the identity provider, never from the request.** A policy reads the claims of the validated access token, which only Keycloak can issue. Nothing the caller sends (a header, a query parameter, a body field) becomes a claim: a claims transformation that copied request input into the principal would let the caller decide their own access. The attribute behind a claim is the identity provider's data, so the user must not be able to change it either: the realm's user profile lets users see `department` but only administrators edit it.

**Which claims the realm maps.** The realm import (`src/AppHost/realms/realm.json`) maps one sample claim:

| Claim | Source | Mapper | In |
|---|---|---|---|
| `department` | the user attribute `department`, declared in the realm's user profile (`components` → `org.keycloak.userprofile.UserProfileProvider`), editable by administrators only | `oidc-usermodel-attribute-mapper` named `department` on the client `<name>-web` | the access token, and the ID token so the frontend can hide what the user may not use (`useHasClaim`) |

Keycloak's standard claims (`email`, `email_verified`, `preferred_username`, ...) reach the token through its default client scopes and can be required the same way, without a mapper.

**How to add a claim.**

1. Declare the attribute in the user profile: add an entry to the `attributes` list in `kc.user.profile.config` (a JSON document stored as a string), with `"edit": ["admin"]` unless users may set it themselves, in which case it must not decide access.
2. Map it into the access token: add an `oidc-usermodel-attribute-mapper` to `<name>-web`'s `protocolMappers`, with `user.attribute` and `claim.name` set and `access.token.claim` `"true"` (and `id.token.claim` `"true"` if the frontend should see it).
3. Give the local users a value under `attributes` (`"attributes": { "cost-center": ["4711"] }`).
4. Require it in the module's `<Module>Policies` and test it as `<Module>ClaimTests` does.

Keycloak imports a realm only when it does not exist yet: remove the data volume to pick up a changed realm. Production configures its realm itself; the import is development only.

**Sample claim and users** (development only, decided by the repository owner, 2026-11-04): `department`, with the sample value `finance`. `developer` (and `developer-b` with tenancy) is in `finance`{{ if roles }} and holds the role `manager`{{ end }}; `visitor` has no department; `clerk` / `clerk` is in `sales`{{ if roles }} and holds `manager`, so it has only the role{{ end }}; `analyst` / `analyst` is in `finance`{{ if roles }} and holds no role, so it has only the claim{{ end }}. With tenancy, `visitor`, `clerk` and `analyst` are in `tenant-a`, so a 403 is about the claim, not the tenant. Every module's sample policy `<module>.manage` requires {{ if roles }}the role `manager` and {{ end }}`department=finance`, on the endpoint `GET /<module>/permissions/manage`.

**Only Keycloak so far.** Another identity provider needs its own way to put a department into the token; with one of them a module's generated claim tests do not compile (`#error`) until this pack learns it.

## Gates

- `OnlyModulePoliciesCheckClaims` parses every source under `src/` and fails, naming file and line, for `HasClaim` anywhere except inside a claims transformation (`IClaimsTransformation`), and `RequireClaim` outside a type implementing `IModulePolicies`.
- `EveryEndpointPolicyIsRegisteredByItsModule` fails for a module endpoint whose policy that module does not register, or which another module registers too, for claim policies as for any other.
- The integration tests (`<Module>ClaimTests`) prove each module's sample policy against the running stack: {{ if roles }}`developer` (role and claim) 200; `clerk` (only the role), `analyst` (only the claim) and `visitor` (neither) 403{{ else }}`developer` and `analyst` 200; `clerk` (another department) and `visitor` (no department) 403; an anonymous caller 401{{ end }}.

## What the gates cannot see

- A claim read with `FindFirst`, `FindAll` or `Claims` and then compared: reading a claim to show it is allowed, and the parse cannot tell showing from deciding. Review's to catch.
- A check written as `RequireAssertion(...)` without `HasClaim`, a claim check through reflection, or in code compiled only under an `#if` the parse leaves disabled.
- A claims transformation that adds claims from request input: it is exempt from the gate because it shapes the claims every policy reads.
- Whether a user may edit the attribute behind a claim: that is the realm's user profile, not code.
- Whether a policy is the right one for its endpoint, or strong enough.

## Consequences

- A permission is one line in the module's `<Module>Policies`; changing who meets it is a change of the user's attributes in the identity provider, not in code.
- A new claim is a change in the realm (user profile, mapper, users) and in production's realm too.
- Tokens grow with every mapped claim; map only what a policy or the frontend needs.
