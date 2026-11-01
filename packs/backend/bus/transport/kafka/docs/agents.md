## Message transport: Kafka

- The broker comes from Aspire; never hard-code a bootstrap server. The connection string is `messaging`.
- Do not create topics or consumer groups by hand; the messaging library's conventions create them.
- Never rely on the order of two events of different types: Kafka orders within a partition only.
