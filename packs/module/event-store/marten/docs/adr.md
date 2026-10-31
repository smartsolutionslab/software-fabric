# Event store: Marten

**Status:** Accepted (generated)

## Decision

Event-sourced modules keep their events in Marten (MIT) on the project's PostgreSQL. Each such module has its own Marten store in its own schema, the one its DbContext uses, so one module's streams and documents are never another's. Events are serialized with System.Text.Json; one stream per aggregate, keyed by the aggregate's id.

The module's `<Module>AggregateStore` (Infrastructure, `EventStore/`) implements `I<Module>AggregateStore` over that store and checks the expected stream version on every save. Projections in the module's Infrastructure project (`SingleStreamProjection<TReadModel, Guid>` and the other Marten projection types) are registered with the store and run inline: a read model changes in the same transaction as the events that change it.

Only a module's Infrastructure project references Marten; an architecture rule fails the build otherwise.

The store creates and updates its tables on first use (`AutoCreate.CreateOrUpdate`). The EF Core migration runner does not apply Marten's schema yet; until it does, the database user the host runs as needs the right to create tables in the module's schema.

Built without tenancy only: generating it with a tenancy option fails, because the streams are not separated by tenant yet.

## Consequences

- A stream is append-only; an event's type name is part of the stored data, so renaming an event type needs a Marten event type alias.
- Integration events still go through the module's unit of work, a separate transaction from the event store's. Save the aggregate first, then publish: a crash in between leaves the change without its integration event, never an event without its change.
