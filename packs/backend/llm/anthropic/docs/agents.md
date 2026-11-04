## LLM: Anthropic

- The project calls Claude through the Anthropic API, with the official `Anthropic` package. Only `src/BuildingBlocks/Llm/Wiring/AnthropicChatClient.cs` names it; every other piece of code takes `IChatClient`.
- The key is the AppHost's secret parameter `anthropic-api-key`. Set it with `dotnet user-secrets set Parameters:anthropic-api-key <key> --project src/AppHost` locally and in the deployment's secret store elsewhere; never write it anywhere in the repository, a log or a test.
- The model is the AppHost's parameter `anthropic-model`, `claude-haiku-5-5` by default. A use case that needs more reasoning is a decision to change it there, to `claude-sonnet-5-5` or `claude-opus-5-5`, at a higher price per token; never hard-code a model id in a module.
- Need streaming, tools or structured output? Use `IChatClient`'s own (`GetStreamingResponseAsync`, `ChatOptions.Tools`, `GetResponseAsync<T>`), not the SDK's types.
