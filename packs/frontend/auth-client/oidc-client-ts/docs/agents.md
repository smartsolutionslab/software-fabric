## Browser sign-in: oidc-client-ts

- Read the signed-in user with `useAuth()` from `react-oidc-context`; never parse tokens yourself.
- Do not add the `Authorization` header by hand; `src/api/baseApi.ts` does it for every generated endpoint.
- Local users: `developer` / `developer` (and `developer-b` with tenancy), defined in `src/AppHost/realms/realm.json`.
