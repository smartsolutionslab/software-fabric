# Topology: services

**Status:** Accepted (generated)

## Decision

Every module runs in a host of its own, `src/Services/<Module>/Host`. The host's project references exactly its own module's Api project, so the assembly scan (`ModuleDiscovery`) finds exactly one `IModule`. Grouping several modules into one host is out of scope: a grouping would be a list in a shared file.

- What a pack contributes to "the host" (`host.*` slots) renders into every service host: authentication, data access, the bus, the mediator, errors. Code packs write once for every host lives in `src/Host` (wiring, host-wide types), which has no project of its own: each service host compiles it.
- The AppHost adds every service host it finds under `src/Services/*/Host/Host.csproj`, named after its module (the project's `<ModuleName>`), and applies the same wiring to each (database, broker, identity provider). It references the service hosts by the same glob, so building the AppHost builds them. Neither the AppHost nor anything else has a line per module.
- Modules talk to each other only through integration events over the broker; `src/Contracts` stays one project, which services reference, never each other.

## Consequences

- `fabric add-module` adds a service: the module and its host are new files, nothing existing changes.
- One .NET process per module; `dotnet run --project src/AppHost` starts them all.
- Two architecture rules keep the topology: no service host references another module's projects, and every service host finds exactly one `IModule`.
- Cross-module calls are events, never in-process calls: `module.communication: contract-interface` is not available.
