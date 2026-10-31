## Event store: Marten

- Load and save aggregates only through `I<Module>AggregateStore`; never open a Marten session in Application or Api code. Only Infrastructure references Marten, and an architecture rule checks it.
- Each event-sourced module has its own Marten store in its own schema; never read another module's streams or documents.
- A projection is a class in the module's Infrastructure project deriving from a Marten projection type (`SingleStreamProjection<InvoiceSummary, Guid>`); it is picked up and runs inline by itself. Never register one by hand.
- Save the aggregate before publishing the integration event that announces it.
- Never edit or delete stored events, and never rename an event type without a Marten alias for the old name.
