# Message transport: Azure Service Bus

**Status:** Accepted (generated)

## Decision

Azure Service Bus carries messages between modules (and later between services). In development Aspire runs the Azure Service Bus emulator (`RunAsEmulator()`) with the SQL Server it keeps its entities in; the host receives the emulator's address as the connection string `messaging`. Queues follow the messaging library's conventions and are created on startup through the management API. The emulator answers management requests on a port of its own, so the AppHost also passes `messaging-management`; against Azure the one connection string serves both, and the host uses `messaging` for management when `messaging-management` is not set.

Versions and licences, checked on 2026-11-08; each version had been published for more than a week:

| Component | Version | Published | Licence |
|---|---|---|---|
| `Aspire.Hosting.Azure.ServiceBus` (AppHost) | 13.6.4 | 2026-10-24 | MIT |
| `WolverineFx.AzureServiceBus` (host), with `Azure.Messaging.ServiceBus` 7.20.1 | 6.59.1 | 2026-10-30 | MIT |
| `Testcontainers.ServiceBus` (with `backend.test.integration` testcontainers) | 4.16.0 | 2026-10-08 | MIT |
| Emulator image `mcr.microsoft.com/azure-messaging/servicebus-emulator`, chosen by Aspire | 2.0.0 | 2026-01-12 | Microsoft's emulator licence (EULA), not open source |
| `mcr.microsoft.com/mssql/server`, chosen by Aspire | 2022-latest | moving tag | Microsoft SQL Server licence, Developer edition (not for production) |

Running the emulator accepts both images' licences (`ACCEPT_EULA=Y`): Aspire sets it, and the Testcontainers fixture accepts it explicitly. Both licences allow development and testing only; production uses a real Azure Service Bus namespace.

## Consequences

- The emulator is for development and tests only: it keeps at most 50 queues and topics per namespace and has no portal; queues it creates live as long as its container.
- The emulator and its SQL Server take about 2.7 GB of images, and SQL Server wants 2 GB of memory; the emulator takes about half a minute to become healthy.
- Deploying to Azure needs a Service Bus namespace whose connection string may manage entities, since the messaging library creates its queues at startup.
