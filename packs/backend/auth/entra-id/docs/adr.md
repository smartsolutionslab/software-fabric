# Authentication: Microsoft Entra ID

**Status:** Accepted (generated)

## Decision

The application accepts Microsoft Entra ID access tokens (v2.0). The host validates JWT bearer tokens with ASP.NET Core's own JwtBearer handler, not Microsoft.Identity.Web, against the signing keys Entra ID publishes for every directory (`AzureAd:Instance`, `https://login.microsoftonline.com` in production), for the audience `AzureAd:Audience`: the API's application ID URI, `api://<name>-api`. Every module's routes require an authenticated user by default.

**One Entra ID directory per tenant, and only the listed ones.** `AzureAd:Tenants:<tenant> = <directory id>` lists the directories whose users may sign in{{ if project["backend.tenancy"] != "none" }}, each one tenant's{{ end }}. The host accepts a token only when its issuer is one of them (`<instance>/<directory id>/v2.0`): a token of any other directory, a valid Entra ID token included, is refused as having an unknown issuer and never reaches a module.

**The application reads Entra ID's claims under the names Entra ID gives them.** The JwtBearer handler renames nothing (`MapInboundClaims = false`). What the application reads, and so what the app registration must put into the access token:

| Claim | Read by | Holds |
|---|---|---|
| `oid` / `sub` | whatever records who owns or did something | the user's stable identifier |
| `preferred_username` | `GET /me` | the user's name |
| `tid` | the issuer check{{ if project["backend.tenancy"] != "none" }}; the tenancy's `HttpTenantContext`, which maps it to the tenant through `AzureAd:Tenants`{{ end }} | the directory that issued the token |
{{~ if project["backend.authorization"] | array.contains "roles" ~}}
| `roles` | `EntraRoleClaims`, which copies them into `ClaimTypes.Role` for the modules' policies; the browser reads them from the ID token | the app roles assigned to the user, a string array (also in the ID token) |
{{~ end ~}}
{{~ if project["backend.authorization"] | array.contains "claims" ~}}
| `department` | the modules' claim policies | the sample claim, an optional claim or a claims-mapping policy in Entra ID (also in the ID token) |
{{~ end ~}}

The browser signs in with the public PKCE client `<name>-web` at `<instance>/organizations/v2.0`, so a user of any listed directory can.

**Locally, a stand-in plays Entra ID.** `src/IdentityStandIn` is a small ASP.NET project that serves Entra ID's v2.0 endpoints under Entra ID's paths: the discovery documents, the signing keys, the token endpoint (the authorization code with PKCE, and the resource owner password grant for the tests) and a sign-in form. It issues tokens shaped like Entra ID's, from two directories with fixed IDs (`00000000-0000-0000-0000-00000000000a` for `tenant-a`, `…0b` for `tenant-b`), signed with a key it makes when it starts. The AppHost runs it as the resource `identity`, in Development only (it refuses to start in any other environment), and passes the host its address as `AzureAd:Instance`, the audience and the two directories. It is never deployed and no real Entra ID tenant is needed to develop or test.

**Only the AppHost knows the stand-in.** `OnlyTheAppHostRunsTheStandIn` fails, naming the file, for any project but the AppHost that references `src/IdentityStandIn`, and, naming file and line, for a source under `src/` but the AppHost's and the stand-in's that names it or Keycloak (`IdentityStandIn`, `keycloak`, `realm_access`, `resource_access`, `/realms/`). Comments are prose and are not checked.

**Test users are part of the provider's contract.** The stand-in has the users the tests sign in as, passwords equal to their names (`src/IdentityStandIn/appsettings.json`): `developer`{{ if project["backend.tenancy"] != "none" }} and `developer-b` (directories of `tenant-a` and `tenant-b`){{ end }}, and, where roles, claims or dashboards need them, `visitor`, `clerk` and `analyst`. `outsider` belongs to a third directory the host does not list; `DirectoryTests` checks that their token is refused. The integration tests sign users in with the password grant at the token endpoint the stand-in's discovery document names. With `backend.test.integration` `testcontainers`, the fixture runs the built stand-in in a container of its own (`mcr.microsoft.com/dotnet/aspnet`) and gives the in-process host its address, audience and directories.

**Every endpoint declares its authorization.** As with every identity provider, an endpoint requires authorization (`RequireAuthorization()` or a policy, inherited from its group or its own) or is anonymous through an explicit `.AllowAnonymous()` where it is mapped; `EveryEndpointDeclaresItsAuthorization` starts the host in Development and in Production and fails for every endpoint with neither.

## Consequences

- **No end-to-end tests.** Nothing signs a browser in at Entra ID or at the stand-in in CI (the generator's decision D8, roadmap M3-26): the integration tests check token validation, tenants, roles and claims against the stand-in, and the web app's unit tests use sample ID tokens. The stand-in's sign-in form exists for local development; the browser flow against a real Entra ID tenant is checked by hand.
- **The stand-in is not Entra ID.** It checks what the host relies on (issuers, audience, signature, claims) and no more: no consent, no conditional access, no refresh tokens, no group overage, no v1.0 tokens. Its password grant also takes a `lifetime`, which Entra ID's does not, so the tests can see a token expire.
- Production registers the API in Entra ID with the application ID URI `api://<name>-api`, `requestedAccessTokenVersion` 2, its app roles and the optional claims above, and the web client `<name>-web` as a single-page application. Where Entra ID puts the API's client ID rather than its URI into `aud`, `AzureAd:Audience` is that ID. Each tenant's directory consents to the application and is added to `AzureAd:Tenants`.
- The stand-in issues an access token for the API whatever scope the browser asks for; Entra ID issues one only for a scope of the API (`api://<name>-api/<scope>`). {{ if project["frontend.auth-client"] == "oidc-client-ts" }}The browser's oidc-client-ts configuration (`web/app/src/auth/oidcConfig.ts`) asks for none, so against a real Entra ID it needs that scope added; this is not checked anywhere, for want of end-to-end tests.{{ end }}
- A directory joins or leaves by configuration alone; no code names one.
- The architecture tests reference the host project, so they build and start it. With the host referenced, a bare `Host` in the architecture tests names the project's `<Name>.Host` namespace; write `Microsoft.Extensions.Hosting.Host` there.
