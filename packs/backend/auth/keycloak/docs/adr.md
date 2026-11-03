# Authentication: Keycloak (OIDC)

**Status:** Accepted (generated)

## Decision

Keycloak is the identity provider. In development Aspire runs it as a container and imports `src/AppHost/realms/realm.json`: a realm named after the project, a public PKCE client for the frontend (`<name>-web`), the API audience (`<name>-api`) and a `developer` user.

In development Keycloak serves plain HTTP (`WithoutHttpsCertificate()`): Aspire 13 would otherwise give it the ASP.NET developer certificate, and its readiness check then depends on that certificate being trusted on the machine.

The host validates JWT bearer tokens with ASP.NET Core's own JwtBearer handler; the AppHost passes it the realm's authority. Every module's routes require an authenticated user by default.

**Every endpoint declares its authorization.** An endpoint requires authorization (`RequireAuthorization()` or a policy, inherited from its group or its own) or is anonymous through an explicit `.AllowAnonymous()` where it is mapped. There is no list of exceptions elsewhere: the reason for an anonymous endpoint goes in the pull request that adds it. The packs' own anonymous endpoints say so the same way: `/health` and `/alive` (`MapDefaultEndpoints`) and the OpenAPI document; `/me` requires authorization. Decided by the repository owner (2026-11-01).

The architecture rule `EveryEndpointDeclaresItsAuthorization` starts the host in-process twice, in Development and in Production (`WebApplicationFactory`, every hosted service removed, placeholder connection strings), reads its `EndpointDataSource` and fails for every endpoint with neither authorization metadata nor `AllowAnonymous`, naming its method and route. An endpoint mapped only in another environment (`IsEnvironment("Staging")`) is not seen. It checks that a declaration exists, not that it is the right one: a policy too weak for its endpoint is review's to catch.

Aspire's Keycloak hosting integration is a preview package (13.6.4-preview); it is used only in the AppHost, which runs nothing in production. The host itself uses no preview package.

## Consequences

- An endpoint open to anonymous callers says so with `.AllowAnonymous()`, visibly, in its own code; one that says nothing fails the architecture tests.
- The architecture tests reference the host project, so they build and start it; startup code that needs a reachable dependency before its hosted services run would break the rule, not only the host. With the host referenced, a bare `Host` in the architecture tests names the project's `<Name>.Host` namespace; write `Microsoft.Extensions.Hosting.Host` there.
- Production needs a real Keycloak and its authority URL in `Authentication:Authority`.
