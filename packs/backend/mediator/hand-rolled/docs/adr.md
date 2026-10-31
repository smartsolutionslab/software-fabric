# Mediator: hand-rolled

**Status:** Accepted (generated)

## Decision

Commands and queries are records implementing `ICommand<TResult>` / `IQuery<TResult>`; each has exactly one handler implementing `ICommandHandler<,>` / `IQueryHandler<,>` in the same module's Application project. Endpoints send them through `IDispatcher`. Handlers are found per module by assembly scan; there is no registration list.

No mediator library: the whole mechanism is three interfaces and a dispatcher in `src/BuildingBlocks/Messaging`, so there is no licence to track and nothing hidden.

## Consequences

- Pipeline behaviours (logging, validation, transactions) are decorators written here when a need appears, not a library feature waiting to be configured.
