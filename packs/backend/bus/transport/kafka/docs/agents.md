## Message transport: Kafka

- The broker comes from Aspire; never hard-code a bootstrap server. The connection string is `messaging`.
- Do not create topics or consumer groups by hand; the messaging library's conventions create them.
{{ if project["backend.topology"] == "services" }}- Each service consumes in a consumer group of its own, named after its module, which the AppHost passes as `Messaging:ConsumerGroup`; never give two services one group name, or each event reaches only one of them.
{{ end }}- Never rely on the order of two events of different types: Kafka orders within a partition only.
