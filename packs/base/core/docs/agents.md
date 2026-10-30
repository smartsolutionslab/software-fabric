## Project structure (base)

- Build everything with `dotnet build build.proj`. Run locally with `dotnet run --project src/AppHost`.
- `src/AppHost` is the only composition root. A new database, broker or service is an Aspire resource there, never a hand-written connection string.
- Each bounded context is a module under `src/Modules/<Module>/` with Domain, Application, Infrastructure and Api projects. Add one with `fabric add-module <name>`; never copy another module.
- Modules never reference each other. They talk through `src/Contracts`.
- Nothing may need a line per module in a shared file (no module list in Program.cs, no per-module ProjectReference). Registration is by convention: implement `IModule` once per module.
- `src/Kernel` holds what every project may use, the Domain included: guards, results, absence. It is referenced and imported everywhere; never add a dependency to it.
- A new package version goes in `Directory.Packages.props`; projects reference it without a version.
- An architecture rule is a class implementing `IArchitectureRule` in `tests/Architecture.Tests/Rules/`; it runs with the other tests.
- Code that wires a choice into the host or into every module is an extension method in the `Microsoft.Extensions.DependencyInjection` (services) or `Microsoft.AspNetCore.Builder` (app) namespace, so no shared file needs a `using` per choice.
