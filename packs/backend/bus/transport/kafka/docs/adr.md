# Message transport: Kafka

**Status:** Accepted (generated)

## Decision

Kafka carries messages between modules (and later between services). In development Aspire runs a single-node broker with a data volume and Kafka UI; the host receives the bootstrap servers as the connection string `messaging`. Topics follow the messaging library's conventions and are created on startup.

## Consequences

- Kafka keeps messages after they are read, so a consumer added later can start from the beginning of a topic; consumers stay idempotent all the same.
- Kafka UI (linked from the Aspire dashboard) shows topics, consumer groups and their lag while developing.
- Ordering holds within a partition only; nothing in a module may rely on two events of different types arriving in order.
