# Messaging library: Wolverine

**Status:** Accepted (generated)

## Decision

Wolverine (MIT) carries integration events. Each module's unit of work is Wolverine's EF Core outbox over the module's DbContext: queued events are stored in the same transaction as the module's changes and sent after commit; incoming messages go through a durable inbox. Envelopes are kept in the `wolverine` schema of the project's database.

{{ if project["backend.bus.transport"] == "kafka" }}Over Kafka every integration event has a topic named after its full type name (`{{ namespace }}.Contracts.Billing.InvoicePaidV1`); the host publishes each event to its topic and listens, as one consumer group named after the project, to the topics of the events its modules consume.

{{ end }}{{ if project["backend.bus.transport"] == "azure-service-bus" }}Over Azure Service Bus every integration event has a queue named after its full type name, created at startup through the management API; the host publishes each event to its queue and listens to the queues of the events its modules consume.

{{ end }}Wolverine discovers only consumers (`*Consumer`) in the modules' Application projects. Commands and queries stay with the project's own mediator; Wolverine is not used as a second one.

A consumer that throws is retried with backoff before its message is given up, as the in-process outbox does: the retries come 1, 2, 4, 8, 16 and 32 seconds after the failure before them, seven attempts within about a minute. Each retry is scheduled in the durable inbox rather than waited for in memory, so it survives a restart and the listener goes on delivering other messages. A message whose seventh attempt fails moves to `wolverine.wolverine_dead_letters` with its exception; setting `replayable = true` on its row delivers it again. A minute rides out a database restart, a failover or a broker reconnect; a failure that outlasts it is no longer transient and belongs in front of a developer. The outbox, which keeps a failed message in the module's own table, tries ten times over about eight and a half minutes; Wolverine stops sooner because its dead letters are the place a developer looks, and replays from.

Wolverine generates handler code at startup (`WolverineFx.RuntimeCompilation`). Production deployments should pre-generate it with `dotnet run --project src/Host -- codegen write` and static type loading; wiring that into the release build is open.

## Consequences

- No event is lost when the broker is down: it waits in the outbox.
- Delivery is at least once, and a failed consumer runs again: consumers must be idempotent.
- A consumer that fails for good is dead-lettered after about a minute, not at once.
