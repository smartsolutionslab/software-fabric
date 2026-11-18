## Topology: services

- Every module runs in its own service host, `src/Services/<Module>/Host`, which references only that module's Api project. Never reference another module's projects from a service host; `ServicesDoNotReferenceEachOther` and `EveryServiceHostRunsExactlyOneModule` fail the build.
- `fabric add-module <name>` creates the module and its service host. Never add a service by hand, and never list services anywhere: the AppHost finds `src/Services/*/Host/Host.csproj` and names each after its `<ModuleName>`.
- A service host's `Program.cs` stays as generated. Code every host needs (wiring, middleware) belongs to the pack that needs it, through the `host.*` slots and `src/Host`, which every service host compiles; `src/Host` has no project and no `Program.cs` of its own.
- Every service has a database of its own on the one server, named after its module, which it reads as the connection string `database`; the AppHost adds it, and the service's own MigrationRunner run, inside its loop. Never point a service at another module's database, add a connection string by hand, or add a line per module to the AppHost.
- A module tells other modules what happened with an integration event in `src/Contracts`; there are no in-process calls between services.
- The frontend and the tests call the edge, `src/Edge`, never a service directly. The edge routes `/<module>/...` to the module's service by its name; never add a route, a module list or a module's name to the edge or the AppHost.
- What the application has once (`/me`, a hub, MCP, AG-UI) is mapped on the edge through the `edge.*` slots, never in a service host; `SingletonsLiveOnTheEdge` fails the build otherwise.
