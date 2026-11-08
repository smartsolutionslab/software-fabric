# API client: openapi-fetch

**Status:** Accepted (generated)

## Decision

The frontend calls the backend through one typed client, `api` in `src/api/baseApi.ts`, made with openapi-fetch. Its types come from the backend's OpenAPI document: the document is checked in as `web/app/openapi.json`, and `pnpm codegen` runs openapi-typescript over it into `src/api/generated.ts` before every `dev`, `build` and `test`; the generated file is not checked in. There are no generated functions or hooks, only types: a call names its method and path as the document has them, `api.GET('/billing/notes')`, and its parameters, body and answer are typed from there.

`api` owns the base URL (`/api`), the request headers (the signed-in user's token is added before every call) and the answer: the body parsed as JSON for a 2xx (nothing for an empty one), an `ApiError` with the status for anything else, so a failed call rejects{{ if project["frontend.state"] == "pinia" }} and the store action that made it catches it{{ else }}, as a TanStack Query `queryFn` must for the query to report its `error`{{ end }}. Endpoints therefore answer JSON, as typed results do; a `text/plain` answer would not parse.

The document is checked in rather than produced while building: the host only starts with its database, broker and identity provider, so it cannot describe itself during a plain build. Instead the integration tests, which run the whole application, compare the checked-in copy with what the host serves. A stale copy fails the test, which also rewrites the file so the fix is one commit away.

The client is a plain function of the document, so it pairs with TanStack Query's caches (React's or Vue's): a screen wraps a call in `useQuery` / `useMutation` itself.{{ if project["frontend.state"] == "pinia" }} It pairs with Pinia stores as well, as here: a store's action makes the call and keeps the answer.{{ end }} With Redux Toolkit, whose server data is RTK Query, the API client is rtk-query-codegen; the catalog refuses openapi-fetch there.

## Consequences

- Changing an endpoint means: run the integration tests (or fetch `/openapi/v1.json` from the running host), commit the rewritten `openapi.json`, run `pnpm codegen`, adapt the frontend; `tsc -b` in `pnpm build` then fails wherever a call names a path, method, parameter or field the document no longer has.
{{~ if project["frontend.state"] != "pinia" ~}}
- Query keys are written by hand, as the path a call names (`['/billing/notes']`, with its parameters after it); nothing generates them.
{{~ end ~}}
- What the gate cannot see: it compares the document only. A hand-written `fetch` to the API, or a header set outside `src/api/baseApi.ts`, passes it; those are for review.
