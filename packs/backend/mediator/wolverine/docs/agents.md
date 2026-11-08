## Mediator: Wolverine

- A use case is a record implementing `ICommand<TResult>` (changes state) or `IQuery<TResult>` (reads), in the module's Application project under `Commands/` or `Queries/`.
- Its handler implements `ICommandHandler<TCommand, TResult>` or `IQueryHandler<TQuery, TResult>` in the same module. Exactly one handler per request; handlers are discovered, never registered by hand.
- A handler is a public class with a public `HandleAsync` method: Wolverine does not invoke an internal handler or an explicit interface implementation, and an architecture rule fails the tests for either.
- Endpoints call `IDispatcher.SendAsync` / `QueryAsync`; they hold no business logic. Behind it Wolverine's bus invokes the handler inline; never call `IMessageBus` for a command or query yourself.
- `CancellationToken` is the last parameter of every handler method.
