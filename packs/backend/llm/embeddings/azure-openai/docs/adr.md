# Embeddings: Azure OpenAI

**Status:** Accepted (generated)

## Decision

The project's embeddings come from an embedding model deployed in its own Azure OpenAI resource, through the resource's OpenAI v1 endpoint (ADR-0012 of the generator).

- **Microsoft's `Microsoft.Extensions.AI.OpenAI` package** supplies the generator: its `IEmbeddingGenerator<string, Embedding<float>>` adapter (`AsIEmbeddingGenerator`) over the official `OpenAI` package's `EmbeddingClient`, calling `<endpoint>/openai/v1/`. It is registered once, in `src/BuildingBlocks/Llm/Wiring/AzureOpenAIEmbeddingGenerator.cs`, with logging and OpenTelemetry around it; no other code names either package. When the project's chat model is Azure OpenAI's or OpenAI's too, the package and its version are the chat pack's.
- **The endpoint and the key are the parameters `azure-openai-endpoint` (no default) and `azure-openai-api-key` (a secret)**, handed to the host as `AzureOpenAI__Endpoint` and `AzureOpenAI__ApiKey`. When the chat model is Azure OpenAI's, they are the same resource and key; otherwise the AppHost declares them for embeddings. The project does not provision the resource.
- **An endpoint that already ends in `/v1` is used as is**, so the same generator runs against any OpenAI-compatible v1 endpoint, OpenAI's own among them. To run locally on a plain OpenAI key, set `azure-openai-endpoint` to `https://api.openai.com/v1`, `azure-openai-api-key` to the OpenAI key and `azure-openai-embedding-deployment` to an OpenAI embedding model's name. Any other endpoint gets `/openai/v1/` appended. This is configuration of the one provider this project chose, not a switch between providers.
- **The deployment is a parameter**, `azure-openai-embedding-deployment`, defaulting to `text-embedding-3-small` (1536 dimensions). An Azure deployment's name is whatever its owner typed, so the value must match a deployment in your resource. Stored vectors have the size of the model that made them, so a model of another size needs vector tables of that size and every record embedded again.
- **Endpoint, deployment and key are read when the generator is first needed.** The host starts without them, and the first use fails naming the missing parameter; registering the generator sends no request.

## Consequences

- An architecture rule fails when the host's `Program.cs` does not call `AddAzureOpenAIEmbeddingGenerator`, and resolves the registered generator, without calling it, failing when it is not the configured deployment at `<endpoint>/openai/v1/`, or at an endpoint ending in `/v1` as given, not wrapped with logging and OpenTelemetry, or when a missing endpoint or key does not name its parameter.
- The integration tests give the endpoint their stand-in, `FakeEmbeddingsServer` on the loopback interface, whose address ends in `/v1`, and the key a value Azure does not accept; they never call the model. No test needs an Azure subscription or a real key.
- Every text embedded is a request to your Azure resource: it costs money and sends the text there, records and questions alike.
