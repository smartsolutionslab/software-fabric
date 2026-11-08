# Event store: KurrentDB

**Status:** Accepted (generated)

## Decision

Event-sourced modules keep their events in KurrentDB (formerly EventStoreDB), a database built for event streams. In development Aspire runs a single insecure node with a data volume through the Community Toolkit's hosting integration (`CommunityToolkit.Aspire.Hosting.KurrentDB` 13.6.1, MIT, published 2026-10-26; image `docker.kurrent.io/kurrent-latest/kurrentdb:26.1.1`); the host receives it as the connection string `eventstore` and talks to it through the official client (`KurrentDB.Client` 1.6.1, Apache-2.0, published 2026-10-27). Every module shares that one client.

Each module has its own category of streams, named after the module (`billing`, or `billing_2` for `billing-2`: KurrentDB's category ends at the first hyphen): one stream per aggregate, `<category>-<aggregate id>`. An event is stored as System.Text.Json under its type's name (`InvoicePaid`), so two domain events of one module may not share a name; the module fails to start if they do.

The module's `<Module>AggregateStore` (Infrastructure, `EventStore/`) implements `I<Module>AggregateStore` over KurrentDB and appends with the stream revision the aggregate was loaded at, so a save from a copy another save has overtaken fails (`WrongExpectedVersionException`). Other adapters over the event store are classes in the module's Infrastructure project whose constructor takes `ModuleEventStore`; each is registered as the module's Application interfaces it implements.

Only a module's Infrastructure project references KurrentDB; an architecture rule fails the build otherwise.

KurrentDB creates a stream on its first append, so the MigrationRunner has nothing to create for it.

Built without tenancy only: the catalog refuses it with a tenancy option, because the streams are not separated by tenant yet. Physical CQRS is refused too: its read models are Marten projections.

## Consequences

- KurrentDB itself is licensed under the Kurrent License v1, which is source-available, not open source: it may be used, changed and run for the project's own application, but not offered to others as a hosted or managed KurrentDB service. Its clients and the Aspire integration are open source (Apache-2.0, MIT).
- A stream is append-only, and an event's type name is part of the stored data: renaming an event type, or moving it to another module, orphans the stored events of the old name.
- Every event-sourced module of a project keeps its events in the same store: the Marten and KurrentDB packs own the same building blocks (`src/BuildingBlocks/EventStore`), so a project mixing them fails to generate.
- A module's events are not projected into read models here; a query reads the aggregate through `I<Module>AggregateStore` or a model of its own.
- Integration events still go through the module's unit of work, a separate transaction from the event store's. Save the aggregate first, then publish: a crash in between leaves the change without its integration event, never an event without its change.
- The AppHost runs the Aspire integration's default image, which a newer integration package brings along. The Testcontainers fixture, when chosen, names the same tag itself and is updated by hand with it.
