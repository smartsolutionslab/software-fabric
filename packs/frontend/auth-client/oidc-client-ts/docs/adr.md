{{~ authorization = object.has_key(project, "backend.authorization") ? project["backend.authorization"] : [] ~}}
{{~ roles = authorization | array.contains "roles" ~}}
{{~ claims = authorization | array.contains "claims" ~}}
# Browser sign-in: oidc-client-ts

**Status:** Accepted (generated)

## Decision

The browser signs in with the authorization-code flow and PKCE through `oidc-client-ts`, wrapped by `react-oidc-context`. The whole app sits behind `SignedIn`: an anonymous visitor is sent to the identity provider. Every API call carries the user's access token, added in `src/api/baseApi.ts`.

The identity provider's address arrives as `VITE_OIDC_AUTHORITY` (the AppHost sets it); the client id is the realm's `<project>-web` client.

{{~ if roles || claims ~}}
**Showing and hiding by {{ if roles && claims }}role and claim{{ else if roles }}role{{ else }}claim{{ end }}.** With `backend.authorization` {{ if roles && claims }}`[roles, claims]`{{ else if roles }}`[roles]`{{ else }}`[claims]`{{ end }}, `src/auth/` has {{ if roles }}`useHasRole(role)`, {{ end }}{{ if claims }}`useHasClaim(type, value?)`, {{ end }}and `<RequirePermission{{ if roles }} role{{ end }}{{ if claims }} claim value{{ end }} fallback>`, which renders its children for a user who meets the requirement{{ if roles && claims }} (both, when `role` and `claim` are given together){{ end }} and the fallback, nothing by default, for anyone else. They read the ID token's claims (`useAuth().user.profile`) synchronously: no request, no token parsing. The Keycloak realm puts {{ if roles }}the realm roles (`realm_access.roles`, realm roles only, not the API client's roles){{ end }}{{ if roles && claims }} and {{ end }}{{ if claims }}`department`{{ end }} into the ID token for this. The source, the realm roles and the API were decided by the repository owner (2026-11-04).

Hiding is a convenience, never protection: the browser and its ID token are the user's to change, and the backend policy decides. A user who should not see a button gets a 403 from its endpoint whether or not the button is hidden. The ID token is read at sign-in, so a role or claim granted later shows once the user signs in again (or the token is renewed); the backend sees it on the next access token.

## Gates

`src/auth/RequirePermission.test.tsx` shows an element to {{ if roles }}`developer` and hides it from `visitor` (role `manager`){{ end }}{{ if roles && claims }}, {{ end }}{{ if claims }}shows it to `analyst` and hides it from `clerk` (`department=finance`){{ end }}{{ if roles && claims }}, and shows one requiring both only to `developer`, not to `clerk` (only the role) or `analyst` (only the claim){{ end }}, with `useAuth` mocked to the ID token claims of the realm's local users. It cannot see whether an endpoint behind a hidden element has a policy: that is the backend's gate.

{{~ end ~}}
## Consequences

- Tokens live in session storage: closing the tab signs the user out of the app, not out of the identity provider.
- Components read the user with `useAuth()`; nothing else talks to the identity provider.
