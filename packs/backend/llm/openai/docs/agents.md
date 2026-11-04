## LLM: OpenAI

- The project calls OpenAI's models through the OpenAI API, with Microsoft's `Microsoft.Extensions.AI.OpenAI` adapter over the official `OpenAI` package. Only `src/BuildingBlocks/Llm/Wiring/OpenAIChatClient.cs` names them; every other piece of code takes `IChatClient`.
- The key is the AppHost's secret parameter `openai-api-key`. Set it with `dotnet user-secrets set Parameters:openai-api-key <key> --project src/AppHost` locally and in the deployment's secret store elsewhere; never write it anywhere in the repository, a log or a test.
- The model is the AppHost's parameter `openai-model`, `gpt-5-mini` by default. A use case that needs more reasoning is a decision to change it there, to `gpt-5`, at a higher price per token; never hard-code a model id in a module.
- Need streaming, tools or structured output? Use `IChatClient`'s own (`GetStreamingResponseAsync`, `ChatOptions.Tools`, `GetResponseAsync<T>`), not the SDK's types.
