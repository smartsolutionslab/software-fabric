## CQRS: physical

- A command (`Commands/<UseCase>/`) loads and saves aggregates through `I<Module>AggregateStore` and returns at most an identifier or nothing.
- A query (`Queries/<UseCase>/`) reads only through `I<Module>ReadModels` (`FindAsync<InvoiceSummary>(id)`, `ListAsync<InvoiceSummary>()`); it never takes an aggregate store, an aggregate or a DbContext, not even inside a `Func<>`, a factory or a list, and never an `IServiceProvider` or `IServiceScopeFactory`. An architecture rule checks it. It checks the dependencies a query and its base classes declare, not what code reaches through static calls or reflection, so do not reach the write side that way either.
- A read model is a plain class with a `Guid Id` under the Application project's `ReadModels/`; a projection feeding it lives in the Infrastructure project's `Projections/`. One read model per screen or question, not one per aggregate.
- Commands and queries are never mixed in one handler.
