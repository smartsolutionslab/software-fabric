## Message transport: RabbitMQ

- The broker comes from Aspire; never hard-code an AMQP address. The connection string is `messaging`.
- Do not declare exchanges or queues by hand; the messaging library's conventions create them.
