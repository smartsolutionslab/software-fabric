# CQRS: none

**Status:** Accepted (generated)

## Decision

A module has one model. Its use cases read and write the same entities through the same DbContext; a use case that changes state may return what it changed, and a read may load the entities it shows. There is no write model kept apart from a read model, and no read store fed by projections.

## Consequences

- Fewest moving parts: one entity, one table, one mapping per concept.
- A read that outgrows the write model (reports, search, heavy joins) is the signal to choose CQRS for that module (`logical` or `physical`), not to grow a read model inside it; an architecture rule fails the build when a type named `*ReadModel` or `*Projection` appears in a module.
