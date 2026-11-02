## API protocol: REST with OpenAPI

- Every endpoint has typed request and response models so the OpenAPI document (`/openapi/v1.json`) describes it fully; the frontend client is generated from that document.
- Use nouns and HTTP verbs: `GET /billing/invoices/{id}`, `POST /billing/invoices`. Return `201` with a location for creates.
- A list endpoint (a `GET` answering a collection) takes `Page? page, PageSize? pageSize` from the query and answers `PagedResult<T>`: `endpoints.MapGet("/invoices", async (Page? page, PageSize? pageSize, IDispatcher dispatcher, CancellationToken cancellationToken) => TypedResults.Ok(await dispatcher.QueryAsync(new ListInvoices(page ?? Page.First, pageSize ?? PageSize.Default), cancellationToken)))`. A page size above `PageSize.Maximum` is a `400` without your code. Never answer a plain array; `ListEndpointsArePagedTests` (integration tests) fails it.
