# Topology: services

**Status:** Accepted (generated)

## Decision

Every module runs in a host of its own, `src/Services/<Module>/Host`. The host's project references exactly its own module's Api project, so the assembly scan (`ModuleDiscovery`) finds exactly one `IModule`. Grouping several modules into one host is out of scope: a grouping would be a list in a shared file.

- What a pack contributes to "the host" (`host.*` slots) renders into every service host: authentication, data access, the bus, the mediator, errors. Code packs write once for every host lives in `src/Host` (wiring, host-wide types), which has no project of its own: each service host compiles it.
- The AppHost adds every service host it finds under `src/Services/*/Host/Host.csproj`, named after its module (the project's `<ModuleName>`), and applies the same wiring to each (database, broker, identity provider). It references the service hosts by the same glob, so building the AppHost builds them. Neither the AppHost nor anything else has a line per module.
- A service host knows its module's route name at runtime: `ServiceModule.Name` (`src/Host/Wiring`) reads the `ModuleName` assembly metadata its project writes from the same `<ModuleName>`, so a service maps what is its own under `/<module>/...` with no configuration and no line per module. Assembly metadata rather than a setting from the AppHost's loop: hosts started without the AppHost (architecture rules, test hosts) know it too.
- `src/Edge` is the one origin the frontend, the OpenAPI copy and the end-to-end tests talk to: a YARP host of its own. Its route table is built at startup from `Edge:Modules`, which the AppHost fills inside its loop with each service's module name, so `/<module>/...` goes to the service service discovery finds under that name, and nothing lists the modules. The edge forwards the caller's bearer token unchanged; every service validates it itself, for one audience.
- What the application has once lives on the edge (`edge.*` slots): `/me`, and the OpenAPI document, which is the edge's own with every service's paths and schemas added (in Development, as the services serve theirs). The edge compiles `src/Host` as the services do, but runs no module.
- A database per service on one server (ADR-0017, D2). The database pack declares the PostgreSQL or SQL Server server alone; inside its loop the AppHost gives each service a database of its own, named after its module (the resource `<module>-database`), and a run of `src/MigrationRunner` of its own (`<module>-migrations`), both under the connection name `database` the service already reads. The run migrates that module alone, named in `Migrations:Module`: its EF Core migrations and its event store's tables. Wolverine's outbox and inbox live in the service's database too. A service starts once its run has completed.
- Modules talk to each other only through integration events over the broker; `src/Contracts` stays one project, which services reference, never each other.

## Consequences

- `fabric add-module` adds a service: the module and its host are new files, nothing existing changes.
- One .NET process per module; `dotnet run --project src/AppHost` starts them all.
- Four architecture rules keep the topology: no service host references another module's projects, every service host finds exactly one `IModule`, every service host knows its module's route name, and no service host maps what the application has once (`/me`, a hub, AG-UI), nor an MCP server at its root (`/mcp`): a service's own MCP server is at `/<module>/mcp`.
- No module can query another's tables: they are in another database. A read model that spans modules is built from integration events.
- Tests that need a database of their own (the read connection's, the vector index's) get one more database on the server, which no module's run migrates.
- A request to a module passes the edge: one more hop, and the edge must be running for the frontend to reach any service.
- Cross-module calls are events, never in-process calls: `module.communication: contract-interface` is not available.
