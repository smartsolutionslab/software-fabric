# API protocol: REST with OpenAPI

**Status:** Accepted (generated)

## Decision

The host describes its HTTP API with ASP.NET Core's built-in OpenAPI generator at `/openapi/v1.json` (Development only). The frontend's API client is generated from this document, so it is the contract between backend and frontend.

## Consequences

- An endpoint's request and response types are part of a published contract; renaming a property is a breaking change for the frontend.
