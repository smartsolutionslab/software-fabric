# LLM: Ollama

**Status:** Accepted (generated)

## Decision

The project calls a local model, served by Ollama.

- **The `OllamaSharp` package** supplies the client: its `OllamaApiClient` is an `IChatClient` itself, with the provider name `ollama`. It is the package Microsoft's own `Microsoft.Extensions.AI.Ollama` (deprecated, preview only) points to; it is a community package, not published by Microsoft or Ollama. It is registered once, in `src/BuildingBlocks/Llm/Wiring/OllamaChatClient.cs`, through the base's `AddModelChatClient`, so logging and OpenTelemetry wrap every call; no other code names it.
- **The AppHost runs Ollama** with `CommunityToolkit.Aspire.Hosting.Ollama`: the container `ollama` (the image `ollama/ollama` at the tag the package pins), with a data volume, and the model resource `ollama-model`, which pulls the model when the AppHost starts. The volume keeps the model, so it is downloaded once per machine. The host gets the server's endpoint as `Ollama__Endpoint` and the model as `Ollama__Model`, and starts once the model is pulled.
- **The model is `llama3.2:1b`** (about 1.3 GB, with tool calling), a constant in `src/AppHost/AppHost.cs`: small enough to pull and run on a developer's machine without a GPU. `qwen3:0.6b` is smaller; `llama3.2` (3B) answers better. Changing the name there changes the model without changing other code. The container runs on the CPU; `.WithGPUSupport()` on it uses an Nvidia GPU.
- **There is no key.** The server is local and unauthenticated.
- **Endpoint and model are read when the client is first needed.** A host started without them starts, and the first use fails naming the AppHost resource that supplies the missing one; registering the client sends no request.

## Consequences

- An architecture rule fails when the host's `Program.cs` does not call `AddOllamaChatClient` (module tests register a stand-in model, so they would not notice), and resolves the registered client, without calling it, failing when it is not Ollama with the configured endpoint and model, not wrapped with logging and OpenTelemetry, or when a missing endpoint does not name its AppHost resource.
- The integration tests remove the `ollama` and `ollama-model` resources before the application starts and give the host an endpoint nothing listens on; they start the application but never pull the image or a model, and never call one.
- The first start of the AppHost downloads the image and the model, gigabytes in all, and a model answers as fast as the machine allows. Nothing leaves the machine: a prompt goes to the local server only.
