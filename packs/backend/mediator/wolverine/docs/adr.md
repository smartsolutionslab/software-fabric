# Mediator: Wolverine

**Status:** Accepted (generated)

## Decision

Commands and queries are records implementing `ICommand<TResult>` / `IQuery<TResult>`; each has exactly one handler implementing `ICommandHandler<,>` / `IQueryHandler<,>` in the same module's Application project. Endpoints send them through `IDispatcher`, as with the hand-rolled mediator; only what is behind it differs.

`IDispatcher` sends every request through Wolverine's bus (`IMessageBus.InvokeAsync`), the same Wolverine the messaging library runs in the host for integration events. Wolverine finds the handlers in the modules' Application projects by their interfaces, besides the consumers, and invokes the one for a request inline, on the caller's async flow (so the signed-in user and the tenant are the caller's), with dependencies Wolverine resolves for that message rather than from the caller's service scope, and returns its result. The request is not sent over the broker and is not stored in the outbox.

The Domain and Application projects still do not reference Wolverine: handlers implement the project's own interfaces, and only `Dispatcher` in `src/BuildingBlocks/Messaging` knows the bus. Handlers are also registered in each module's services, so a module's tests validate their dependencies without a running host.

The option needs the Wolverine messaging library (`backend.bus.library == wolverine`), so a project without a broker cannot choose it; there the hand-rolled mediator is the only one.

A handler may itself take `IDispatcher`, as one that runs an agent or a dashboard widget's query does. Wolverine gets it from the message's scope rather than building it, and the dispatcher takes the bus when it is created, so a result that sends requests after the handler has returned, a stream of an agent's updates, still reaches the bus once Wolverine has disposed that scope.

Tests that send requests through `IDispatcher` start Wolverine in process, with no broker and no message store: the agents', the assistant's and the dashboards' tests in a service container of their own, and the gRPC and MCP server tests in the host, where every other hosted service stays removed. Each finds its handlers with the host's discovery, `DiscoverRequestHandlers()`.

## Consequences

- Pipeline behaviours (logging, validation, transactions) can be Wolverine middleware and policies instead of hand-written decorators.
- Wolverine generates the code that calls a handler at startup, as it does for consumers (see the messaging library's ADR for pre-generating it in production).
- Code that runs a handler without the host, in a test, sends it through `IDispatcher` only with Wolverine started; without it, it calls the handler itself. Wolverine compiles a handler's code at runtime, so every test project references `WolverineFx.RuntimeCompilation`.
