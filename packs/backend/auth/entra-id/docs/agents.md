## Authentication: Microsoft Entra ID

- The host accepts Entra ID access tokens for `AzureAd:Audience` from the directories `AzureAd:Tenants` lists (`AzureAd:Tenants:<tenant> = <directory id>`), and from no other. A new tenant is a new directory there, never code.
- Read claims by the names Entra ID gives them (`oid`, `preferred_username`, `tid`{{ if project["backend.authorization"] | array.contains "roles" }}, `roles`{{ end }}); claims are not renamed on the way in, so never read them by `ClaimTypes.*` except `ClaimTypes.Role`, which the role mapping fills.
- Locally, `src/IdentityStandIn` stands in for Entra ID; its users and directories are in its `appsettings.json`. Only the AppHost references it, and no source under `src/` but the AppHost's and its own names it; the architecture rule `OnlyTheAppHostRunsTheStandIn` fails the build otherwise. A claim the application needs is Entra ID's to issue: add it to the stand-in locally, and document it in docs/adr for the app registration.
- Every module route requires an authenticated user. Open an endpoint to anonymous callers only with an explicit `.AllowAnonymous()` where it is mapped, and a reason in the PR; the architecture rule `EveryEndpointDeclaresItsAuthorization` fails the build for an endpoint that declares neither.
- `GET /me` answers who the caller is (`name`); the frontend uses it.
- Authorisation rules are policies registered by the module, not role checks inside handlers.
- There are no end-to-end tests of the sign-in; prove sign-in behaviour with an integration test against the stand-in.
- Local login: user `developer`, password `developer`, at the stand-in (run by the AppHost, development only).
{{~ if project["frontend.auth-client"] == "msal" ~}}
- MSAL signs the browser in at the stand-in's https address (`https://localhost:8443`): run `dotnet dev-certs https --trust` once on your machine. The host accepts tokens issued there through `AzureAd:BrowserInstance`, which only the AppHost sets; never set it in production.
{{~ end ~}}
