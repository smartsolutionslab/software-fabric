## Topology: services

- Every module runs in its own service host, `src/Services/<Module>/Host`, which references only that module's Api project. Never reference another module's projects from a service host; `ServicesDoNotReferenceEachOther` and `EveryServiceHostRunsExactlyOneModule` fail the build.
- `fabric add-module <name>` creates the module and its service host. Never add a service by hand, and never list services anywhere: the AppHost finds `src/Services/*/Host/Host.csproj` and names each after its `<ModuleName>`.
- A service host's `Program.cs` stays as generated. Code every host needs (wiring, middleware) belongs to the pack that needs it, through the `host.*` slots and `src/Host`, which every service host compiles; `src/Host` has no project and no `Program.cs` of its own.
- A module tells other modules what happened with an integration event in `src/Contracts`; there are no in-process calls between services.
