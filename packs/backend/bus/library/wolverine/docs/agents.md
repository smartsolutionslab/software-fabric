## Messaging library: Wolverine

- Do not reference Wolverine from Domain or Application code; publish through `I<Module>UnitOfWork`, consume with a `*Consumer` class.
- A consumer may take dependencies by constructor or as extra `Handle` parameters (Wolverine injects them), and returns `void` or `Task`; one that awaits is named `HandleAsync`.
- Local runs need the broker and database from Aspire: `dotnet run --project src/AppHost`.
