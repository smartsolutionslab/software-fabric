# API client: generated RTK Query endpoints

**Status:** Accepted (generated)

## Decision

The frontend's API client is generated from the backend's OpenAPI document by `@rtk-query/codegen-openapi`. The document is checked in as `web/app/openapi.json`; `pnpm codegen` turns it into `src/api/generated.ts` and runs before every `dev`, `build` and `test`; the generated file is not checked in. The generated endpoints are injected into `src/api/baseApi.ts`, which owns the base URL (`/api`) and the credentials.

The document is checked in rather than produced while building: the host only starts with its database, broker and identity provider, so it cannot describe itself during a plain build. Instead the integration tests, which run the whole application, compare the checked-in copy with what the host serves. A stale copy fails the test, which also rewrites the file so the fix is one commit away.

## Consequences

- Changing an endpoint means: run the integration tests (or fetch `/openapi/v1.json` from the running host), commit the rewritten `openapi.json`, run `pnpm codegen`, adapt the frontend.
- Hand-written `fetch` calls to the API are not used; the types would drift.
