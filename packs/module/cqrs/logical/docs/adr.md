# CQRS: logical

**Status:** Accepted (generated)

## Decision

Writes and reads are separate use cases — commands under `Commands/`, queries under `Queries/` in the Application project — over the same database. A command changes state through the domain model and returns at most an identifier; a query reads with projections straight to DTOs and never loads an aggregate to change it.

No separate read store; that is the physical variant.

## Consequences

- Queries can be optimised (projections, no tracking) without touching the write model.
