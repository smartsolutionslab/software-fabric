## Authentication: Keycloak

- Every module route requires an authenticated user. Open an endpoint to anonymous callers only with an explicit `.AllowAnonymous()` and a reason in the PR.
- `GET /me` answers who the caller is (`name`); the frontend uses it.
- Authorisation rules are policies registered by the module, not role checks inside handlers.
- Local login: user `developer`, password `developer` (realm import in `src/AppHost/realms/realm.json`, development only).
