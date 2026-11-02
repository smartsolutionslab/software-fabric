# CQRS: logical

**Status:** Accepted (generated)

## Decision

Writes and reads are separate use cases — commands under `Commands/`, queries under `Queries/` in the Application project — over the same database. A command changes state through the domain model and returns at most an identifier; a query reads with projections straight to DTOs and never loads an aggregate to change it.

A model becomes a DTO through extension methods: `To<Name>Dto()` in a `<Model>Mappings` static class beside the DTO, and, for an EF `Select`, `To<Name>DtoExpression()` returning an `Expression<Func<Model, Dto>>`, because EF cannot translate a method call to SQL. Handlers never build a DTO themselves; an architecture rule reads each handler's compiled code, its lambdas and state machines included, and fails the build when it constructs a `*Dto`.

No separate read store; that is the physical variant.

## Consequences

- Queries can be optimised (projections, no tracking) without touching the write model.
- Every handler that returns the same DTO uses the same mapping, so copies cannot drift apart.
- The rule recognises a DTO by its name (`*Dto`) and looks only at code inside the handler type; a DTO built by another class the handler calls is not seen.
