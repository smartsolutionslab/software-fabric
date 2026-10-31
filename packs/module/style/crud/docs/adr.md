# Module style: CRUD

**Status:** Accepted (generated)

## Decision

CRUD modules store and return data with few rules of their own. Their entities are plain classes in the module's Domain project: an identifier and properties, no aggregate roots, no domain events, no base classes. EF Core maps them in the Infrastructure project with one `IEntityTypeConfiguration<T>` per entity, so the Domain project needs neither EF Core nor ASP.NET Core; an architecture rule fails the build if it references either.

Validation sits where the data arrives: request models are validated in the use case before an entity is created or changed.

## Consequences

- A screen-shaped module (lists, forms, settings) costs one entity, one configuration and its use cases.
- When a module's rules grow (invariants spanning several entities, state that may only change in certain ways), it is time for another module style, chosen with an ADR of its own; keeping entities free of persistence makes that move cheaper.
