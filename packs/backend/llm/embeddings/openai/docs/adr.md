# Embeddings: OpenAI

**Status:** Accepted (generated)

## Decision

The project's embeddings come from OpenAI's embedding models, through the OpenAI API (ADR-0012 of the generator).

- **Microsoft's `Microsoft.Extensions.AI.OpenAI` package** supplies the generator: its `IEmbeddingGenerator<string, Embedding<float>>` adapter (`AsIEmbeddingGenerator`) over the official `OpenAI` package's `EmbeddingClient`. It is registered once, in `src/BuildingBlocks/Llm/Wiring/OpenAIEmbeddingGenerator.cs`, with logging and OpenTelemetry around it; no other code names either package. When the project's chat model is OpenAI's or Azure OpenAI's too, the package and its version are the chat pack's.
- **The key is the Aspire secret parameter `openai-api-key`**, handed to the host as `OpenAI__ApiKey`. When the chat model is OpenAI's, it is the same key; otherwise the AppHost declares it for embeddings. Locally it comes from the AppHost's user secrets (`dotnet user-secrets set Parameters:openai-api-key <key> --project src/AppHost`); in a deployment, from its secret store.
- **The model is a parameter**, `openai-embedding-model`, defaulting to `text-embedding-3-small` (1536 dimensions). `text-embedding-3-large` (3072) is stronger, at a higher price; stored vectors have the size of the model that made them, so a model of another size needs vector tables of that size and every record embedded again.
- **Key and model are read when the generator is first needed.** The host starts without them, and the first use fails naming the missing parameter; registering the generator sends no request.

## Consequences

- An architecture rule fails when the host's `Program.cs` does not call `AddOpenAIEmbeddingGenerator`, and resolves the registered generator, without calling it, failing when it is not OpenAI with the configured model, not wrapped with logging and OpenTelemetry, or when a missing key does not name its parameter.
- The integration tests give `openai-api-key` a value OpenAI does not accept; they never call the model. No test needs a real key.
- Every text embedded is a request to OpenAI: it costs money and sends the text there, records and questions alike.
