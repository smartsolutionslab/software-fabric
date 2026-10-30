## Module communication: integration events

- To tell other modules something happened, add a record `<Something>V1 : IIntegrationEvent` under `src/Contracts/<Module>/`, publish it with `I<Module>UnitOfWork.PublishAsync(...)` and then `CommitAsync(...)`. Never publish outside the unit of work.
- To react to another module's event, add `<Event>Consumer` with `Handle(<Event> message)` in your module's Application project. Consumers are idempotent: the same event may arrive twice.
- Never edit a published event; add `V2` beside it.
- Name consumers `...Consumer`, never `...Handler`: handlers are for your own commands and queries.
