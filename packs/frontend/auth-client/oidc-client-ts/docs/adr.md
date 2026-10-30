# Browser sign-in: oidc-client-ts

**Status:** Accepted (generated)

## Decision

The browser signs in with the authorization-code flow and PKCE through `oidc-client-ts`, wrapped by `react-oidc-context`. The whole app sits behind `SignedIn`: an anonymous visitor is sent to the identity provider. Every API call carries the user's access token, added in `src/api/baseApi.ts`.

The identity provider's address arrives as `VITE_OIDC_AUTHORITY` (the AppHost sets it); the client id is the realm's `<project>-web` client.

## Consequences

- Tokens live in session storage: closing the tab signs the user out of the app, not out of the identity provider.
- Components read the user with `useAuth()`; nothing else talks to the identity provider.
