# API protocol: REST with OpenAPI

**Status:** Accepted (generated)

## Decision

The host describes its HTTP API with ASP.NET Core's built-in OpenAPI generator at `/openapi/v1.json` (Development only), mapped with `.AllowAnonymous()`: code generation reads it without signing in. The frontend's API client is generated from this document, so it is the contract between backend and frontend.

**List endpoints are paged.** A list endpoint takes `Page? page` and `PageSize? pageSize` from the query (the Kernel's types, defaulting to `Page.First` and `PageSize.Default`) and answers `PagedResult<T>`. `Page` and `PageSize` bind through their `TryParse`, so a value outside its range (a page below 1, a page size above `PageSize.Maximum`, anything that is not a whole number) is a `400` before the endpoint runs, and no caller can ask for the whole table. `DescribePaging()` (`src/Host/Wiring/OpenApiPaging.cs`) describes both parameters as the integer range they bind; without it the generator would call any type it parses a string. The document names the response `PagedResultOf<Item>`, so the generated client has the type. Decided by the repository owner (2026-11-01): paging is required for collection queries behind a list endpoint only; an internal lookup that no endpoint answers with, a small fixed set, may return a plain list.

The integration test `ListEndpointsArePagedTests` reads the OpenAPI document the running host serves and fails for every list endpoint that breaks this.

- **How it recognises a list endpoint:** a `GET` whose `200` answer is a JSON array, or a `PagedResultOf...` schema. A JSON array is a violation outright; a `PagedResult` must have `page` and `pageSize` among its query parameters. The answer decides, not the route: a route cannot tell a collection (`/invoices`) from a single resource at a fixed path (`/me`, `/billing/settings`), and an answer can.
- **What it cannot see:** an endpoint whose answer the document does not describe (one returning a plain `IResult`, or excluded from the description); a list wrapped in an object of another name (`{ "invoices": [...] }`); a collection answered under another status than `200` or another media type than `application/json`; a `GET` on a route ending in a parameter that answers a list; whether `page` and `pageSize` really page the query behind the endpoint rather than being accepted and ignored. Those are review's to catch.

## Consequences

- An endpoint's request and response types are part of a published contract; renaming a property is a breaking change for the frontend.
- A fixed lookup that is exposed over HTTP (currencies, countries) is a list endpoint too and is paged; only a lookup no endpoint answers with may return a plain list.
