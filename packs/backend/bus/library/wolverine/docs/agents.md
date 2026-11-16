## Messaging library: Wolverine

- Do not reference Wolverine from Domain or Application code; publish through `I<Module>UnitOfWork`, consume with a `*Consumer` class.
- A consumer may take dependencies by constructor or as extra `Handle` parameters (Wolverine injects them), and returns `void` or `Task`; one that awaits is named `HandleAsync`.
- Local runs need the broker and database from Aspire: `dotnet run --project src/AppHost`.
- A consumer that throws is retried with backoff (1, 2, 4 … 32 seconds), then dead-lettered in `wolverine.wolverine_dead_letters`, whichever way it arrived, never in a dead-letter queue of the broker's; `replayable = true` on its row delivers it again. Throw for a failure worth retrying; do not catch and swallow it. Because a retry runs the consumer again, it must be idempotent.
