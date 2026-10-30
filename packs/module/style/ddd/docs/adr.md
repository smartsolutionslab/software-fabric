# Module style: domain-driven design

**Status:** Accepted (generated)

## Decision

Modules model their business with aggregates, entities and domain events. Aggregates derive from `AggregateRoot<TId>`, entities from `Entity<TId>`; both live in the module's Domain project, which depends on nothing but the BCL and `src/BuildingBlocks.Domain`. Invariants are enforced inside the aggregate; nothing outside changes its state except through its methods.

## Consequences

- Persistence, HTTP and messaging never leak into the Domain project; an architecture rule fails the build if they do.
- Each aggregate is loaded and saved as a whole; cross-aggregate consistency is eventual, through domain events.
