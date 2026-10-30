# DDD depth: light

**Status:** Accepted (generated)

## Decision

Aggregates, entities and value objects for the concepts that carry business rules; plain types where nothing would be gained. A value object is introduced when a value has rules of its own (an amount with a currency, an email address), not for every string.

Enforced: aggregate roots expose no public setters. Not enforced: primitives on the domain model — that is the strict depth.

## Consequences

- Less ceremony in simple modules; a module that grows rules adopts value objects as it goes.
