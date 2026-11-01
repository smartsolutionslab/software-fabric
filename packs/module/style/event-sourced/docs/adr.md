# Module style: event-sourced

**Status:** Accepted (generated)

## Decision

An event-sourced module stores what happened, not what is. Its aggregates derive from `EventSourcedAggregate<TId>` (`src/BuildingBlocks.EventSourcing`): every change is raised as a domain event (`Raise(new InvoicePaid(...))`) and applied by the aggregate's own `Apply(InvoicePaid)` method. The events are the aggregate's record; loading it replays them through the same `Apply` methods, so the state after a change and the state after a reload cannot differ.

Identities are records implementing `IAggregateId` over the id of the aggregate's event stream (`InvoiceId(Guid Value)`). Application code loads and saves aggregates through the module's `I<Module>AggregateStore`; the chosen event store implements it in the module's Infrastructure project. A save appends the pending events and fails when another save appended to the stream since the aggregate was loaded.

The module's Domain project depends on nothing but the BCL, `Kernel` and `BuildingBlocks.EventSourcing`.

## Consequences

- A stored event is a fact and is never changed: a new shape is a new event type, and the aggregate keeps an `Apply` for the old one as long as streams contain it.
- An event without an `Apply` would be lost on the next reload; an architecture rule fails the build if one exists, and if an aggregate has a public setter.
- A module mixing this style with `ddd` in one project is not built yet: both packs own the Domain projects' building-block reference and purity rule.
