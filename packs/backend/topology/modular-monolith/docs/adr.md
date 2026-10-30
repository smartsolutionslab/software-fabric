# Topology: modular monolith

**Status:** Accepted (generated)

## Decision

All modules run in one process, `src/Host`. The host references every module's Api project by glob and discovers each module's `IModule` at startup.

## Consequences

- One deployable, one database server, in-process calls where a module exposes a contract interface.
- Module boundaries are enforced by architecture tests, not by the network: a module referencing another module's assemblies fails the build.
- Splitting a module into its own service later means giving it a host of its own; the module code does not change.
