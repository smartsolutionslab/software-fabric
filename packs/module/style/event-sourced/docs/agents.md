## Module style: event-sourced

- An aggregate derives from `EventSourcedAggregate<TId>`, its id is a record implementing `IAggregateId` (`InvoiceId(Guid Value)`), and it has a private parameterless constructor for loading.
- A method named after what happens (`invoice.Pay()`) checks the invariant, then calls `Raise(new InvoicePaid(...))`. State changes only in a private `Apply(InvoicePaid)` method per event, never in the method that raises it, so a reload rebuilds the same state. An architecture rule fails if an event has no `Apply` or an aggregate has a public setter.
- Domain events are records implementing `IDomainEvent`, named in the past tense, in the aggregate's `Events/` folder. A stored event is never edited or removed: add a new event type and keep the old `Apply`.
{{ if project["backend.errors"] == "result" }}- A broken invariant is a value: the method returns `Result<Unit, DomainError>` and raises nothing. Domain errors carry a code and a message, never an HTTP status; the Application maps them to `ApiError`.
{{ end }}- Load and save aggregates in Application handlers through `I<Module>AggregateStore`; a save that lost a race fails, and the command reports it rather than retrying silently.
- The Domain project references nothing but the BCL, `Kernel` and `BuildingBlocks.EventSourcing`.
