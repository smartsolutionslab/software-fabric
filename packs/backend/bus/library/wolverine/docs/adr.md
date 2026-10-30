# Messaging library: Wolverine

**Status:** Accepted (generated)

## Decision

Wolverine (MIT) carries integration events. Each module's unit of work is Wolverine's EF Core outbox over the module's DbContext: queued events are stored in the same transaction as the module's changes and sent after commit; incoming messages go through a durable inbox. Envelopes are kept in the `wolverine` schema of the project's database.

Wolverine discovers only consumers (`*Consumer`) in the modules' Application projects. Commands and queries stay with the project's own mediator; Wolverine is not used as a second one.

Wolverine generates handler code at startup (`WolverineFx.RuntimeCompilation`). Production deployments should pre-generate it with `dotnet run --project src/Host -- codegen write` and static type loading; wiring that into the release build is open.

## Consequences

- No event is lost when the broker is down: it waits in the outbox.
- Delivery is at least once; consumers must be idempotent.
