# Base: an Aspire modular solution

**Status:** Accepted (generated)

## Decision

- **.NET Aspire is the composition root.** `src/AppHost` is the only place runtime resources are added; nothing wires a connection string by hand.
- **Modules are bounded contexts** under `src/Modules/<Module>/`, each split into Domain, Application, Infrastructure and Api projects.
- **Modules register by convention, never by list.** Builds find module projects by glob, the host finds each module's `IModule` by assembly scan, and per-layer packages come from `src/Modules/Directory.Build.props`. Adding a module adds files only.
- **One build for everything:** `build.proj` (Microsoft.Build.Traversal) builds every project under `src/` and `tests/`. `Solution.slnx` is for IDEs and lists the projects present at generation; after `fabric add-module`, run `dotnet sln Solution.slnx add` for the new projects.
- **One dependency-free kernel.** `src/Kernel` carries the building blocks every layer uses (argument guards, result and absence types, as chosen) and is referenced and globally imported by every project but the AppHost.
- **Warnings are errors**, with the recommended analyzers and code style enforced in the build. Package versions are managed centrally in `Directory.Packages.props`.
- **Architecture rules are code.** Each rule implements `IArchitectureRule` in `tests/Architecture.Tests`; the test framework runs all of them.
