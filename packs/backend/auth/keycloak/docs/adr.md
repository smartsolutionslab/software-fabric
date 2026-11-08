# Authentication: Keycloak (OIDC)

**Status:** Accepted (generated)

## Decision

Keycloak is the identity provider. In development the AppHost runs it as a container and imports `src/AppHost/realms/realm.json`: a realm named after the project, a public PKCE client for the frontend (`<name>-web`), the API audience (`<name>-api`) and a `developer` user.

**The AppHost runs Keycloak as a plain container** (`quay.io/keycloak/keycloak`, `start-dev --import-realm`), not through Aspire's Keycloak hosting integration: that integration has no stable release yet, every use of it reports diagnostics the AppHost would have to silence, and the project silences no rule. The container declares what the integration did: its administrator (`admin`, with a password Aspire generates once and keeps in the AppHost's user secrets as the parameter `keycloak-password`), a readiness check on Keycloak's management port (`/health/ready`), the realm directory mounted read-only at `/opt/keycloak/data/import`, and a data volume. It serves plain HTTP on port 8080, so its readiness does not depend on a developer certificate being trusted on the machine.

**Keycloak's email goes to Mailpit in development.** When the AppHost runs (not when it publishes), it adds a Mailpit container and Keycloak waits for it; the realm's `smtpServer` points at `mailpit:1025`, so verification and reset-password emails can be read in Mailpit's inbox at http://localhost:8025 or through its API. Publishing the AppHost adds no Mailpit: production configures the realm's SMTP server itself. The realm import is development-only already (it carries the local users' passwords). Keycloak imports a realm only when it does not exist yet, so a data volume from before this change keeps a realm without SMTP until the volume is removed.

The host validates JWT bearer tokens with ASP.NET Core's own JwtBearer handler; the AppHost passes it the realm's authority. Every module's routes require an authenticated user by default.

**Every endpoint declares its authorization.** An endpoint requires authorization (`RequireAuthorization()` or a policy, inherited from its group or its own) or is anonymous through an explicit `.AllowAnonymous()` where it is mapped. There is no list of exceptions elsewhere: the reason for an anonymous endpoint goes in the pull request that adds it. The packs' own anonymous endpoints say so the same way: `/health` and `/alive` (`MapDefaultEndpoints`) and the OpenAPI document; `/me` requires authorization. Decided by the repository owner (2026-11-01).

The architecture rule `EveryEndpointDeclaresItsAuthorization` starts the host in-process twice, in Development and in Production (`WebApplicationFactory`, every hosted service removed, placeholder connection strings), reads its `EndpointDataSource` and fails for every endpoint with neither authorization metadata nor `AllowAnonymous`, naming its method and route. An endpoint mapped only in another environment (`IsEnvironment("Staging")`) is not seen. It checks that a declaration exists, not that it is the right one: a policy too weak for its endpoint is review's to catch.

## Consequences

- An endpoint open to anonymous callers says so with `.AllowAnonymous()`, visibly, in its own code; one that says nothing fails the architecture tests.
- The architecture tests reference the host project, so they build and start it; startup code that needs a reachable dependency before its hosted services run would break the rule, not only the host. With the host referenced, a bare `Host` in the architecture tests names the project's `<Name>.Host` namespace; write `Microsoft.Extensions.Hosting.Host` there.
- Production needs a real Keycloak and its authority URL in `Authentication:Authority`, and its realm needs a real SMTP server.
- The integration tests prove the mail path: Keycloak's admin API sends the developer a reset-password email and the test reads it from Mailpit's API.
