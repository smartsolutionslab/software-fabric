{{~ authorization = object.has_key(project, "backend.authorization") ? project["backend.authorization"] : [] ~}}
{{~ roles = authorization | array.contains "roles" ~}}
{{~ claims = authorization | array.contains "claims" ~}}
## Browser sign-in: MSAL

{{~ if project["frontend.framework"] == "vue" ~}}
- Read the signed-in account with `useAuth()` from `@/auth/useAuth`; never parse tokens yourself. It is a small composable over `@azure/msal-browser`'s `PublicClientApplication`; do not add a Vue MSAL library.
- Test a component that reads the account by providing `createAuth(fake)` under `authKey`, as `src/auth/SignedIn.test.ts` does; never mock `@azure/msal-browser` itself.
{{~ else ~}}
- Read the signed-in account with `useMsal()` from `@azure/msal-react`: the user is `instance.getActiveAccount()`, the account the kept access token is for, never `accounts[0]` (MSAL may cache several); never parse tokens yourself.
{{~ end ~}}
- Create no other `PublicClientApplication`: `createMsal()` in `src/auth/msal.ts` is the app's one client, and it keeps the access token that `accessToken()` (`@/auth/accessToken`) reads.
- Do not add the `Authorization` header by hand; `src/api/baseApi.ts` does it for every generated endpoint.
- The authority must be https: msal-common refuses any other, and `src/auth/msalConfig.test.ts` fails for one. Locally, run `dotnet dev-certs https --trust` once, so the browser trusts the stand-in's https address.
- There are no end-to-end tests of the sign-in; prove sign-in behaviour with the unit tests in `src/auth/`, against a fake client.
- Local users: `developer` / `developer` (and `developer-b` with tenancy), defined in {{ local_users_source }}.
{{~ if project["frontend.framework"] != "vue" && (roles || claims) ~}}
- Hiding an element is a convenience, never protection: the backend policy decides. Every action you hide still needs its endpoint's policy (`.RequireAuthorization(...)`).
- Show or hide with `<RequirePermission {{ if roles }}role="manager"{{ else }}claim="department" value="finance"{{ end }} fallback={...}>` from `@/auth/RequirePermission`; it renders nothing by default.{{ if roles && claims }} `role` and `claim` together mean the user needs both.{{ end }}
- For a boolean, {{ if roles }}`useHasRole('manager')` from `@/auth/useHasRole` ({{ role_kind }}s only){{ end }}{{ if roles && claims }} or {{ end }}{{ if claims }}`useHasClaim('department', 'finance')` from `@/auth/useHasClaim` (without a value, any value will do){{ end }}. {{ if roles && claims }}They read{{ else }}It reads{{ end }} the ID token claims of the active account; never decode a token for {{ if roles && claims }}them{{ else }}it{{ end }}.
- A role or claim the frontend should see must be in the ID token: {{ id_token_claim_rule }}
- Test what a screen shows per user by mocking `useMsal` from `@azure/msal-react`, as `src/auth/RequirePermission.test.tsx` does with {{ local_users }}.
{{~ end ~}}
{{~ if project["frontend.framework"] == "vue" && (roles || claims) ~}}
- Hiding an element is a convenience, never protection: the backend policy decides. Every action you hide still needs its endpoint's policy (`.RequireAuthorization(...)`).
- Show or hide with `<RequirePermission {{ if roles }}role="manager"{{ else }}claim="department" value="finance"{{ end }}>` from `@/auth/RequirePermission.vue`: its default slot renders for a user who meets the requirement, its `#fallback` slot (nothing by default) for anyone else.{{ if roles && claims }} `role` and `claim` together mean the user needs both.{{ end }}
- For a boolean, {{ if roles }}`useHasRole('manager')` from `@/auth/useHasRole` ({{ role_kind }}s only){{ end }}{{ if roles && claims }} or {{ end }}{{ if claims }}`useHasClaim('department', 'finance')` from `@/auth/useHasClaim` (without a value, any value will do){{ end }}, a `ComputedRef<boolean>` that follows the account. {{ if roles && claims }}They read{{ else }}It reads{{ end }} the ID token claims of the signed-in account, `useAuth().account`; never decode a token for {{ if roles && claims }}them{{ else }}it{{ end }}.
- A role or claim the frontend should see must be in the ID token: {{ id_token_claim_rule }}
- Test what a screen shows per user by providing, under `authKey`, an `Auth` whose `account` has the ID token claims of {{ local_users }}, as `src/auth/RequirePermission.test.ts` does.
{{~ end ~}}
