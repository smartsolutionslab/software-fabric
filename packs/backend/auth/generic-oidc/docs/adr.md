# Authentication: a generic OpenID Connect provider

**Status:** Accepted (generated)

## Decision

The application accepts the access tokens of any OpenID Connect provider: Auth0, Okta, Zitadel, Authentik, a company's own, or Keycloak. The host validates JWT bearer tokens with ASP.NET Core's own JwtBearer handler against the provider's discovery document at `Authentication:Authority`, for the audience `Authentication:Audience`. Every module's routes require an authenticated user by default.

**The application reads standard claims only, under the names the provider gives them.** The JwtBearer handler renames nothing (`MapInboundClaims = false`). What the application reads, and so what the provider must put into the access token:

| Claim | Read by | Holds |
|---|---|---|
| `sub` | whatever records who owns or did something | the user's stable identifier |
| `preferred_username` | `GET /me` | the user's name |
{{~ if project["backend.authorization"] | array.contains "roles" ~}}
| `roles` | `OidcRoleClaims`, which copies them into `ClaimTypes.Role` for the modules' policies; the browser reads them from the ID token | the user's roles, a string array (also in the ID token) |
{{~ end ~}}
{{~ if project["backend.tenancy"] != "none" ~}}
| `tenant` | the tenancy's `HttpTenantContext` | the user's one tenant; a token naming none or several has no tenant |
{{~ end ~}}
{{~ if project["backend.authorization"] | array.contains "claims" ~}}
| `department` | the modules' claim policies | the sample claim (also in the ID token) |
{{~ end ~}}

The audience is the API's identifier at the provider (`<name>-api` locally); the browser signs in with the public PKCE client `<name>-web`.

**Locally, the provider is Keycloak acting as a plain OpenID Connect provider.** Aspire runs it as a container named `identity` and imports `src/AppHost/realms/realm.json`: a realm named after the project, the two clients, and protocol mappers that put the claims above at the top level of the token. The AppHost is the only place that knows it is Keycloak; the host and the browser receive its authority and nothing else. In development it serves plain HTTP (`WithoutHttpsCertificate()`), so the host does not require HTTPS metadata there. Aspire's Keycloak hosting integration is a preview package used only in the AppHost.

**Test users are part of the provider's contract.** The local provider has the users the tests sign in as, passwords equal to their names: `developer`{{ if project["backend.tenancy"] != "none" }} and `developer-b` (tenants `tenant-a` and `tenant-b`){{ end }}, and, where roles, claims or dashboards need them, `visitor`, `clerk` and `analyst`. The integration tests sign them in with the resource owner password grant at the token endpoint the provider's discovery document names.

**Every endpoint declares its authorization.** An endpoint requires authorization (`RequireAuthorization()` or a policy, inherited from its group or its own) or is anonymous through an explicit `.AllowAnonymous()` where it is mapped; `EveryEndpointDeclaresItsAuthorization` starts the host in Development and in Production and fails for every endpoint with neither.

**The application stays provider-neutral.** `ProviderNeutralHost` parses every source under `src/` but the AppHost and fails, naming file and line, for code that names Keycloak or a claim or path only Keycloak issues or serves (`realm_access`, `resource_access`, `/realms/`). Comments are prose and are not checked.

## Consequences

- Production sets `Authentication:Authority` and `Authentication:Audience` to the real provider, registers the API and the web client there, and configures it to issue the claims above. A provider that names a claim differently maps it on its side (a claim mapper, a custom claim, an action), not in the application.
- `ProviderNeutralHost` checks names, not behaviour: code that reads a provider's quirk under a neutral name is review's to catch.
- Nothing provider-specific is generated for email, account management or user administration: those are the provider's own consoles. The local Keycloak sends no email.
- A token that lives briefly cannot be asked of a provider in a standard way; the tests that need one ask the local Keycloak's admin API, in the tests only.
- The architecture tests reference the host project, so they build and start it. With the host referenced, a bare `Host` in the architecture tests names the project's `<Name>.Host` namespace; write `Microsoft.Extensions.Hosting.Host` there.
