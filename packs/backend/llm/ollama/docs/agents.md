## LLM: Ollama

- The project calls a local model served by Ollama, through the `OllamaSharp` package. Only `src/BuildingBlocks/Llm/Wiring/OllamaChatClient.cs` names it; every other piece of code takes `IChatClient`. Do not add `Microsoft.Extensions.AI.Ollama` (deprecated) or a second Ollama client.
- The AppHost runs the `ollama` container with a data volume and pulls the model `ollama-model` at start; the host waits for it. Start it with `dotnet run --project src/AppHost`; never install or point at an Ollama server by hand.
- The model is `llama3.2:1b`, a constant in `src/AppHost/AppHost.cs`. A use case that needs better answers is a decision to change it there (to `llama3.2`, say), at the cost of a bigger download and slower answers; never hard-code a model name in a module. For a GPU, add `.WithGPUSupport()` to the container there.
- Tests never start Ollama: the integration-test fixture removes its resources and gives the host an endpoint nothing listens on. Keep it that way; a test that needs an answer uses the fake `IChatClient`.
- Need streaming, tools or structured output? Use `IChatClient`'s own (`GetStreamingResponseAsync`, `ChatOptions.Tools`, `GetResponseAsync<T>`), not `OllamaSharp`'s types.
