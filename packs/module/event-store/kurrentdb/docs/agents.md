## Event store: KurrentDB

- Load and save aggregates only through `I<Module>AggregateStore`; never use `KurrentDBClient` in Application or Api code. Only Infrastructure references KurrentDB, and an architecture rule checks it.
- Each event-sourced module has its own category of streams (`<module>-<aggregate id>`); never read or append to another module's streams.
- Another adapter over the event store is a class in the module's Infrastructure project whose constructor takes `ModuleEventStore`; it is registered as its Application interface by itself. Never register one by hand.
- KurrentDB comes from Aspire (`dotnet run --project src/AppHost`) as the connection string `eventstore`; never hard-code an address.
- Save the aggregate before publishing the integration event that announces it.
- Never rename a domain event type or give two events of one module the same name: an event is stored under its type's name.
