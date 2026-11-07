## Authentication: generic OpenID Connect

- The host accepts any OpenID Connect provider: `Authentication:Authority` and `Authentication:Audience` say which. Never name a provider in code under `src/` outside the AppHost; the architecture rule `ProviderNeutralHost` fails the build for `keycloak`, `realm_access`, `resource_access` or `/realms/`.
- Read claims by the names the provider gives them (`sub`, `preferred_username`{{ if project["backend.authorization"] | array.contains "roles" }}, `roles`{{ end }}{{ if project["backend.tenancy"] != "none" }}, `tenant`{{ end }}); claims are not renamed on the way in, so never read them by `ClaimTypes.*` except `ClaimTypes.Role`, which the role mapping fills.
- A claim the application needs is the provider's to issue: add its mapper to `src/AppHost/realms/realm.json` locally, and document it in docs/adr for production's provider.
- Every module route requires an authenticated user. Open an endpoint to anonymous callers only with an explicit `.AllowAnonymous()` where it is mapped, and a reason in the PR; the architecture rule `EveryEndpointDeclaresItsAuthorization` fails the build for an endpoint that declares neither.
- `GET /me` answers who the caller is (`name`); the frontend uses it.
- Authorisation rules are policies registered by the module, not role checks inside handlers.
- Local login: user `developer`, password `developer`, at the local provider (Keycloak, run by the AppHost; realm import in `src/AppHost/realms/realm.json`, development only).
