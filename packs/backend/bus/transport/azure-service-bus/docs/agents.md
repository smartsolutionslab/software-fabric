## Message transport: Azure Service Bus

- The broker comes from Aspire, which runs the emulator in development; never hard-code a namespace or a connection string. The connection string is `messaging`; the emulator's management address is `messaging-management`.
- Do not create queues, topics or subscriptions by hand, in the emulator's configuration or in Azure; the messaging library's conventions create them.
- The emulator holds at most 50 queues and topics together; do not add queues of your own beside the conventional ones.
