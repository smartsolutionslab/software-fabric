# Embeddings: Ollama

**Status:** Accepted (generated)

## Decision

The project's embeddings come from a local embedding model, served by Ollama (ADR-0012 of the generator).

- **The `OllamaSharp` package** supplies the generator: its `OllamaApiClient` is an `IEmbeddingGenerator<string, Embedding<float>>` itself, with the provider name `ollama`. It is registered once, in `src/BuildingBlocks/Llm/Wiring/OllamaEmbeddingGenerator.cs`, with logging and OpenTelemetry around it; no other code names it. When the project's chat model is Ollama's too, the package, its version and the server are the chat pack's.
- **The AppHost runs Ollama** with `CommunityToolkit.Aspire.Hosting.Ollama`: the container `ollama` with a data volume (one server, shared with the chat model when that is Ollama's too), and the model resource `ollama-embedding-model`, which pulls the model when the AppHost starts. The host gets the server's endpoint as `Ollama__Endpoint` and the model as `Ollama__EmbeddingModel`, and starts once the model is pulled.
- **The model is `nomic-embed-text`** (768 dimensions, about 274 MB), a constant in `src/AppHost/AppHost.cs`. Stored vectors have the size of the model that made them, so a model of another size needs vector tables of that size and every record embedded again.
- **There is no key.** The server is local and unauthenticated.
- **Endpoint and model are read when the generator is first needed.** A host started without them starts, and the first use fails naming the AppHost resource that supplies the missing one; registering the generator sends no request.

## Consequences

- An architecture rule fails when the host's `Program.cs` does not call `AddOllamaEmbeddingGenerator`, and resolves the registered generator, without calling it, failing when it is not Ollama with the configured endpoint and model, not wrapped with logging and OpenTelemetry, or when a missing endpoint does not name its AppHost resource.
- The integration tests remove the `ollama` and `ollama-embedding-model` resources before the application starts and give the host their stand-in as its endpoint, `FakeEmbeddingsServer` on the loopback interface, which answers `/api/embed`; they never pull the image or a model, and never call one.
- The first start of the AppHost downloads the image and the model. Nothing leaves the machine: text is embedded by the local server only.
