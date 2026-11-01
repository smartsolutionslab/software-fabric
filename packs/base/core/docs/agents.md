## Project structure (base)

- Run `dotnet tool restore` once after cloning; local tools are pinned in `.config/dotnet-tools.json`.
- Build everything with `dotnet build build.proj`. Run locally with `dotnet run --project src/AppHost`.
- Format with `bash .github/scripts/format.sh` before you push. CI runs it with `--verify-no-changes` and fails on any file `dotnet format` would change, modules and tests included.
- A workflow step's `uses:` names a full 40-character commit SHA with its version tag in a comment (`actions/checkout@<sha> # v4.4.0`), never a tag or branch: a tag can be moved to code nobody reviewed.
- `src/AppHost` is the only composition root. A new database, broker or service is an Aspire resource there, never a hand-written connection string.
- Each bounded context is a module under `src/Modules/<Module>/` with Domain, Application, Infrastructure and Api projects. Add one with `fabric add-module <name>`; never copy another module.
- Modules never reference each other. They talk through `src/Contracts`.
- Nothing may need a line per module in a shared file (no module list in Program.cs, no per-module ProjectReference). Registration is by convention: implement `IModule` once per module.
- `src/Kernel` holds what every project may use, the Domain included: guards, results, absence. It is referenced and imported everywhere; never add a dependency to it.
- A new package version goes in `Directory.Packages.props`; projects reference it without a version.
- An architecture rule is a class implementing `IArchitectureRule` in `tests/Architecture.Tests/Rules/`; it runs with the other tests.
- Code that wires a choice into the host or into every module is an extension method in the `Microsoft.Extensions.DependencyInjection` (services) or `Microsoft.AspNetCore.Builder` (app) namespace, so no shared file needs a `using` per choice.
- HttpClients retry only GET, HEAD, PUT, DELETE, OPTIONS and TRACE; a POST or PATCH gets one attempt. Only a client whose POSTs or PATCHes are idempotent in fact (a token mint) may opt back in with `.RetryEveryMethod()` on its `IHttpClientBuilder`, with a comment at that call site saying why a second attempt cannot apply twice. Never replace the retry predicate in `src/ServiceDefaults`.
