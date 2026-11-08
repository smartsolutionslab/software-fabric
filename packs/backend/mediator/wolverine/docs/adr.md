# Mediator: Wolverine

**Status:** Accepted (generated)

## Decision

Commands and queries are records implementing `ICommand<TResult>` / `IQuery<TResult>`; each has exactly one handler implementing `ICommandHandler<,>` / `IQueryHandler<,>` in the same module's Application project. Endpoints send them through `IDispatcher`, as with the hand-rolled mediator; only what is behind it differs.

`IDispatcher` sends every request through Wolverine's bus (`IMessageBus.InvokeAsync`), the same Wolverine the messaging library runs in the host for integration events. Wolverine finds the handlers in the modules' Application projects by their interfaces, besides the consumers, and invokes the one for a request inline, in the caller's scope, returning its result. The request is not sent over the broker and is not stored in the outbox.

The Domain and Application projects still do not reference Wolverine: handlers implement the project's own interfaces, and only `Dispatcher` in `src/BuildingBlocks/Messaging` knows the bus. Handlers are also registered in each module's services, so a module's tests validate their dependencies without a running host.

The option needs the Wolverine messaging library (`backend.bus.library == wolverine`), so a project without a broker cannot choose it; there the hand-rolled mediator is the only one.

## Consequences

- Pipeline behaviours (logging, validation, transactions) can be Wolverine middleware and policies instead of hand-written decorators.
- Wolverine generates the code that calls a handler at startup, as it does for consumers (see the messaging library's ADR for pre-generating it in production).
- Code that runs a handler without the host, in a test, sends it through `IDispatcher` only with Wolverine started; without it, it calls the handler itself.
