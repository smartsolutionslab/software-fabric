## Authentication: Keycloak

- Every module route requires an authenticated user. Open an endpoint to anonymous callers only with an explicit `.AllowAnonymous()` where it is mapped, and a reason in the PR.
- Every endpoint, on the host or beside a module's group, carries `RequireAuthorization()`, a policy, or `.AllowAnonymous()`. The architecture rule `EveryEndpointDeclaresItsAuthorization` fails the build for one that carries none, naming it; never add a list of exceptions.
- `GET /me` answers who the caller is (`name`); the frontend uses it.
- Authorisation rules are policies registered by the module, not role checks inside handlers.
- Local login: user `developer`, password `developer` (realm import in `src/AppHost/realms/realm.json`, development only).
- Email Keycloak sends locally (verification, reset password) lands in Mailpit: http://localhost:8025 while the AppHost runs. Mailpit exists only in run mode; a deployed realm configures its own SMTP server.
