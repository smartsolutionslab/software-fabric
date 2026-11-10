{{~ authorization = object.has_key(project, "backend.authorization") ? project["backend.authorization"] : [] ~}}
{{~ roles = authorization | array.contains "roles" ~}}
{{~ claims = authorization | array.contains "claims" ~}}
{{~ if project["frontend.framework"] == "vue" ~}}
## Browser sign-in: oidc-client-ts

- Read the signed-in user with `useAuth()` from `@/auth/useAuth`; never parse tokens yourself. It is a small composable over oidc-client-ts's `UserManager`; do not add a Vue OIDC library.
- Do not add the `Authorization` header by hand; `src/api/baseApi.ts` does it for every generated endpoint.
- Test a component that reads the user by providing `createAuth(fake)` under `authKey`, as `src/auth/SignedIn.test.ts` does; never mock oidc-client-ts itself.
- Local users: `developer` / `developer` (and `developer-b` with tenancy), defined in {{ local_users_source }}.
{{~ if roles || claims ~}}
- Hiding an element is a convenience, never protection: the backend policy decides. Every action you hide still needs its endpoint's policy (`.RequireAuthorization(...)`).
- Show or hide with `<RequirePermission {{ if roles }}role="manager"{{ else }}claim="department" value="finance"{{ end }}>` from `@/auth/RequirePermission.vue`: its default slot renders for a user who meets the requirement, its `#fallback` slot (nothing by default) for anyone else.{{ if roles && claims }} `role` and `claim` together mean the user needs both.{{ end }}
- For a boolean, {{ if roles }}`useHasRole('manager')` from `@/auth/useHasRole` ({{ role_kind }}s only){{ end }}{{ if roles && claims }} or {{ end }}{{ if claims }}`useHasClaim('department', 'finance')` from `@/auth/useHasClaim` (without a value, any value will do){{ end }}, a `ComputedRef<boolean>` that follows the user. {{ if roles && claims }}They read{{ else }}It reads{{ end }} the ID token through `useAuth().user`'s `profile`; never decode a token for {{ if roles && claims }}them{{ else }}it{{ end }}.
- A role or claim the frontend should see must be in the ID token: {{ id_token_claim_rule }}
- Test what a screen shows per user by providing, under `authKey`, an `Auth` whose `user` has the ID token claims of {{ local_users }}, as `src/auth/RequirePermission.test.ts` does.
{{~ end ~}}
{{~ else ~}}
## Browser sign-in: oidc-client-ts

- Read the signed-in user with `useAuth()` from `react-oidc-context`; never parse tokens yourself.
- Do not add the `Authorization` header by hand; `src/api/baseApi.ts` does it for every generated endpoint.
- Local users: `developer` / `developer` (and `developer-b` with tenancy), defined in {{ local_users_source }}.
{{~ if roles || claims ~}}
- Hiding an element is a convenience, never protection: the backend policy decides. Every action you hide still needs its endpoint's policy (`.RequireAuthorization(...)`).
- Show or hide with `<RequirePermission {{ if roles }}role="manager"{{ else }}claim="department" value="finance"{{ end }} fallback={...}>` from `@/auth/RequirePermission`; it renders nothing by default.{{ if roles && claims }} `role` and `claim` together mean the user needs both.{{ end }}
- For a boolean, {{ if roles }}`useHasRole('manager')` from `@/auth/useHasRole` ({{ role_kind }}s only){{ end }}{{ if roles && claims }} or {{ end }}{{ if claims }}`useHasClaim('department', 'finance')` from `@/auth/useHasClaim` (without a value, any value will do){{ end }}. {{ if roles && claims }}They read{{ else }}It reads{{ end }} the ID token through `useAuth().user.profile`; never decode a token for {{ if roles && claims }}them{{ else }}it{{ end }}.
- A role or claim the frontend should see must be in the ID token: {{ id_token_claim_rule }}
- Test what a screen shows per user by mocking `useAuth` from `react-oidc-context`, as `src/auth/RequirePermission.test.tsx` does with {{ local_users }}.
{{~ end ~}}
{{~ end ~}}
