# API style: minimal APIs

**Status:** Accepted (generated)

## Decision

HTTP endpoints are minimal APIs. Each endpoint (or small group of related endpoints) is a class implementing `IEndpoint` in the module's Api project; the module maps every `IEndpoint` in its assembly into its route group (`/<module>`). No controllers.

## Consequences

- An endpoint is a small class that translates HTTP to a command or query and back; adding one never edits a shared file.
