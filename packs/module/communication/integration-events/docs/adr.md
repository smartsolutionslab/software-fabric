# Module communication: integration events

**Status:** Accepted (generated)

## Decision

Modules tell each other what happened through integration events: records in `src/Contracts/<Module>/`, implementing `IIntegrationEvent`, named in the past tense with a version suffix (`InvoicePaidV1`). An event is published through the module's unit of work (`I<Module>UnitOfWork.PublishAsync`) and sent only when `CommitAsync` succeeds, so a change and the event announcing it are saved together or not at all.

Another module reacts with a consumer — a class named `<Event>Consumer` in its Application project with a `Handle(<Event>)` method.

## Consequences

- An event's shape is a contract: change it by adding `<Event>V2`, never by editing V1 in place.
- A consumer runs later and possibly more than once; it must be idempotent.
