# Topology: services

**Status:** Accepted (generated)

## Decision

Every module runs in a host of its own, `src/Services/<Module>/Host`. The host's project references exactly its own module's Api project, so the assembly scan (`ModuleDiscovery`) finds exactly one `IModule`. Grouping several modules into one host is out of scope: a grouping would be a list in a shared file.

- What a pack contributes to "the host" (`host.*` slots) renders into every service host: authentication, data access, the bus, the mediator, errors. Code packs write once for every host lives in `src/Host` (wiring, host-wide types), which has no project of its own: each service host compiles it.
- The AppHost adds every service host it finds under `src/Services/*/Host/Host.csproj`, named after its module (the project's `<ModuleName>`), and applies the same wiring to each (database, broker, identity provider). It references the service hosts by the same glob, so building the AppHost builds them. Neither the AppHost nor anything else has a line per module.
- `src/Edge` is the one origin the frontend, the OpenAPI copy and the end-to-end tests talk to: a YARP host of its own. Its route table is built at startup from `Edge:Modules`, which the AppHost fills inside its loop with each service's module name, so `/<module>/...` goes to the service service discovery finds under that name, and nothing lists the modules. The edge forwards the caller's bearer token unchanged; every service validates it itself, for one audience.
- What the application has once lives on the edge (`edge.*` slots): `/me`, and the OpenAPI document, which is the edge's own with every service's paths and schemas added (in Development, as the services serve theirs). The edge compiles `src/Host` as the services do, but runs no module.
- Modules talk to each other only through integration events over the broker; `src/Contracts` stays one project, which services reference, never each other.

## Consequences

- `fabric add-module` adds a service: the module and its host are new files, nothing existing changes.
- One .NET process per module; `dotnet run --project src/AppHost` starts them all.
- Three architecture rules keep the topology: no service host references another module's projects, every service host finds exactly one `IModule`, and no service host maps what the application has once (`/me`, a hub, MCP, AG-UI).
- A request to a module passes the edge: one more hop, and the edge must be running for the frontend to reach any service.
- Cross-module calls are events, never in-process calls: `module.communication: contract-interface` is not available.
