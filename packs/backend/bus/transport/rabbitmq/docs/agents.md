## Message transport: RabbitMQ

- The broker comes from Aspire; never hard-code an AMQP address. The connection string is `messaging`.
- Do not declare exchanges or queues by hand; the messaging library's conventions create them.
{{ if project["backend.topology"] == "services" }}- Each service listens on a queue of its own, named after its module and bound to the exchange of every event it consumes, which the AppHost passes as `Messaging:Queue`; never give two services one queue name, or each event reaches only one of them.
{{ end }}