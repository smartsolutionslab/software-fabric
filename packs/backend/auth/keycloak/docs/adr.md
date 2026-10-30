# Authentication: Keycloak (OIDC)

**Status:** Accepted (generated)

## Decision

Keycloak is the identity provider. In development Aspire runs it as a container and imports `src/AppHost/realms/realm.json`: a realm named after the project, a public PKCE client for the frontend (`<name>-web`), the API audience (`<name>-api`) and a `developer` user.

In development Keycloak serves plain HTTP (`WithoutHttpsCertificate()`): Aspire 13 would otherwise give it the ASP.NET developer certificate, and its readiness check then depends on that certificate being trusted on the machine.

The host validates JWT bearer tokens with ASP.NET Core's own JwtBearer handler; the AppHost passes it the realm's authority. Every module's routes require an authenticated user by default.

Aspire's Keycloak hosting integration is a preview package (13.6.4-preview); it is used only in the AppHost, which runs nothing in production. The host itself uses no preview package.

## Consequences

- An endpoint open to anonymous callers says so with `.AllowAnonymous()`, visibly, in its own code.
- Production needs a real Keycloak and its authority URL in `Authentication:Authority`.
