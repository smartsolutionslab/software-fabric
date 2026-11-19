# Mediator: hand-rolled

**Status:** Accepted (generated)

## Decision

Commands and queries are records implementing `ICommand<TResult>` / `IQuery<TResult>`; each has exactly one handler implementing `ICommandHandler<,>` / `IQueryHandler<,>` in the same module's Application project. Endpoints send them through `IDispatcher`. Handlers are found per module by assembly scan; there is no registration list.

No mediator library: the whole mechanism is three interfaces and a dispatcher in `src/BuildingBlocks/Messaging`, so there is no licence to track and nothing hidden.
{{~ if object.has_key(project, "backend.bus.library") && project["backend.bus.library"] == "wolverine" ~}}

A consumer of integration events may take `IDispatcher` and its module's own services (its unit of work, its aggregate store in an event-sourced module, any interface the module's Application project declares and its Infrastructure project implements) by constructor or as a `Handle` parameter. Wolverine builds consumers in generated code, which cannot build the dispatcher or the unit of work, since both take the service provider, nor an event store's adapters, which a factory makes; with `ServiceLocationPolicy.NotAllowed`, Wolverine's default, it would refuse the consumer (`InvalidServiceLocationException`) and dead-letter every event it handles. So the host has Wolverine take the dispatcher and every such interface of every module from the message's scope (`ResolveDispatcherFromScope`, `ResolveModuleServicesFromScope`), found by convention, so a new module needs no entry anywhere. The dispatcher then resolves the handlers it sends to from that scope as well, so a consumer, its handlers and their stores share one DbContext. Service location stays refused for everything else.
{{~ end ~}}

## Consequences

- Pipeline behaviours (logging, validation, transactions) are decorators written here when a need appears, not a library feature waiting to be configured.
