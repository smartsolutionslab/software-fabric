# Message transport: RabbitMQ

**Status:** Accepted (generated)

## Decision

RabbitMQ carries the integration events this host exchanges with other services. Events between modules of the host do not cross it: the messaging library delivers them through durable local queues in the host (see its ADR). In development Aspire runs it as a container with the management UI and a data volume; the host receives its connection string as `messaging`. Exchanges and queues follow the messaging library's conventions and are created on startup.

## Consequences

- The management UI (linked from the Aspire dashboard) shows queues and stuck messages while developing.
