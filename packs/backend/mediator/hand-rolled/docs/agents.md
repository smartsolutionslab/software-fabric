## Mediator: hand-rolled

- A use case is a record implementing `ICommand<TResult>` (changes state) or `IQuery<TResult>` (reads), in the module's Application project under `Commands/` or `Queries/`.
- Its handler implements `ICommandHandler<TCommand, TResult>` or `IQueryHandler<TQuery, TResult>` in the same module. Exactly one handler per request; handlers are discovered, never registered by hand.
- Endpoints call `IDispatcher.SendAsync` / `QueryAsync`; they hold no business logic.
{{~ if object.has_key(project, "backend.bus.library") && project["backend.bus.library"] == "wolverine" ~}}
- A consumer may take `IDispatcher` and its module's unit of work, aggregate store and other services by constructor, like any dependency. Wolverine takes them from the message's scope (`ResolveDispatcherFromScope`, `ResolveModuleServicesFromScope`, in the host's messaging): never list a module's service for Wolverine by hand, and never allow service location for everything.
{{~ end ~}}
- `CancellationToken` is the last parameter of every handler method.
