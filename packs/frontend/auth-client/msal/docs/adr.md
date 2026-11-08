{{~ authorization = object.has_key(project, "backend.authorization") ? project["backend.authorization"] : [] ~}}
{{~ roles = authorization | array.contains "roles" ~}}
{{~ claims = authorization | array.contains "claims" ~}}
{{~ vue = project["frontend.framework"] == "vue" ~}}
# Browser sign-in: MSAL

**Status:** Accepted (generated)

## Decision

The browser signs in with the authorization-code flow and PKCE through Microsoft's MSAL: `@azure/msal-browser`'s `PublicClientApplication`, created once by `createMsal()` (`src/auth/msal.ts`), {{ if vue }}read through a small hand-written composable, `useAuth()` (`src/auth/useAuth.ts`). `@azure/msal-react` is React's only, and the composable needs only the account, the sign-in state and the two redirects.{{ else }}provided to the components by `@azure/msal-react`'s `MsalProvider`.{{ end }} The whole app sits behind `SignedIn`: an anonymous visitor is sent to the identity provider. Every API call carries the user's access token, added in `src/api/baseApi.ts`.

The identity provider's address arrives as `VITE_OIDC_AUTHORITY` (the AppHost sets it); {{ browser_client_adr }}

**The authority is https.** msal-common refuses every authority that is not (`authority_uri_insecure`), whatever `knownAuthorities`, `authorityMetadata` or the protocol mode say. So the local stand-in also serves https, with the ASP.NET Core development certificate, and the AppHost gives the browser that address; without `VITE_OIDC_AUTHORITY` the app falls back to `{{ browser_authority }}`. **Locally, trust the development certificate once: `dotnet dev-certs https --trust`**, or the browser refuses the stand-in's address and the sign-in never starts. The configuration (`src/auth/msalConfig.ts`) lists the authority in `knownAuthorities`, so MSAL reads its discovery document instead of asking Entra ID about an unknown host, and sets `OIDCOptions.responseMode` to `'query'`: the stand-in answers in the query, and MSAL's default is the fragment.

**The access token outside the components.** `createMsal()` keeps every token MSAL acquires, the sign-in's included, in the tab's session storage (`src/auth/accessToken.ts`), and makes its account the active one; signing out forgets it. `accessToken()` reads it for the API client and for connections outside {{ if vue }}Vue{{ else }}React{{ end }}. It is not renewed silently, because the stand-in issues no refresh token: when it expires, `SignedIn` sends the user back to the identity provider.

{{~ if !vue && (roles || claims) ~}}
**Showing and hiding by {{ if roles && claims }}role and claim{{ else if roles }}role{{ else }}claim{{ end }}.** With `backend.authorization` {{ if roles && claims }}`[roles, claims]`{{ else if roles }}`[roles]`{{ else }}`[claims]`{{ end }}, `src/auth/` has {{ if roles }}`useHasRole(role)`, {{ end }}{{ if claims }}`useHasClaim(type, value?)`, {{ end }}and `<RequirePermission{{ if roles }} role{{ end }}{{ if claims }} claim value{{ end }} fallback>`, which renders its children for a user who meets the requirement{{ if roles && claims }} (both, when `role` and `claim` are given together){{ end }} and the fallback, nothing by default, for anyone else. They read the signed-in account's ID token claims (`useMsal().accounts[0].idTokenClaims`): no request, no token parsing. {{ id_token_claims_adr roles claims }}

Hiding is a convenience, never protection: the browser and its ID token are the user's to change, and the backend policy decides.

{{~ end ~}}
{{~ if vue && (roles || claims) ~}}
**No showing and hiding by role or claim on Vue yet.** As with oidc-client-ts, `RequirePermission` and its hooks are React's only so far (the generator's ADR-0006); `src/auth/RequirePermission.test.ts` fails, saying so, until Vue has them.

{{~ end ~}}
## Gates

`src/auth/msalConfig.test.ts` runs msal-browser itself against the configuration the app is built with, `fetch` answering as the stand-in: the authority is https, a sign-in goes to the stand-in's authorization endpoint with PKCE and `response_mode=query`, and coming back with a code signs the user in and keeps the access token. Built with an http `VITE_OIDC_AUTHORITY`, these tests fail with `authority_uri_insecure`, so a non-https authority cannot slip back in. `src/auth/SignedIn.test.{{ if vue }}ts{{ else }}tsx{{ end }}` checks, against a fake MSAL client, that an anonymous visitor (or one whose access token expired) is sent to the identity provider, that {{ if vue }}the way back completes the sign-in{{ else }}nothing starts while MSAL completes the way back{{ end }}, that a failed sign-in or a redirect that cannot start is shown and not retried, that signing out starts no sign-in on its way out, and that a signed-in user sees who they are and the app.{{ if !vue && (roles || claims) }} `src/auth/RequirePermission.test.tsx` shows and hides elements per user, with `useMsal` mocked to the ID token claims of {{ local_users }}.{{ end }}

## Consequences

- **No end-to-end tests.** Nothing signs a browser in through MSAL in CI (the generator's decision D8, roadmap M3-44): the unit tests above stand in, and the browser flow is checked by hand, locally against the stand-in and against a real Entra ID tenant. End-to-end tests of other choices that sign a user in at the stand-in need the development certificate trusted by the browser that runs them.
- Tokens live in session storage: closing the tab signs the user out of the app, not out of the identity provider.
- After an hour, when the access token expires, the user signs in again; with a real Entra ID tenant its session cookie usually makes that a redirect without a form. Silent renewal would need refresh tokens, which the stand-in does not issue.
- Components read the user {{ if vue }}with `useAuth()`{{ else }}with `useMsal()`{{ end }}; nothing else talks to the identity provider.
