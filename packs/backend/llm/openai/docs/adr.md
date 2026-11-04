# LLM: OpenAI

**Status:** Accepted (generated)

## Decision

The project calls OpenAI's models through the OpenAI API.

- **Microsoft's `Microsoft.Extensions.AI.OpenAI` package** supplies the client: its `IChatClient` adapter (`AsIChatClient`) over the official `OpenAI` package (by OpenAI), which it brings with it. It is registered once, in `src/BuildingBlocks/Llm/Wiring/OpenAIChatClient.cs`, through the base's `AddModelChatClient`, so logging and OpenTelemetry wrap every call; no other code names either package.
- **The key is an Aspire secret parameter**, `openai-api-key`, which the AppHost hands to the host as `OpenAI__ApiKey`. Locally it comes from the AppHost's user secrets (`dotnet user-secrets set Parameters:openai-api-key <key> --project src/AppHost`); in a deployment, from its secret store.
- **The model is a parameter**, `openai-model`, defaulting to `gpt-5-mini`: fast and cheap enough for the questions a generated sample asks. `gpt-5` answers harder questions at a higher price; changing the parameter's value changes the model without changing code.
- **Key and model are read when the client is first needed.** The host starts without them, and the first use fails naming the missing parameter; registering the client sends no request.

## Consequences

- An architecture rule fails when the host's `Program.cs` does not call `AddOpenAIChatClient` (module tests register a stand-in model, so they would not notice), and resolves the registered client, without calling it, failing when it is not OpenAI with the configured model, not wrapped with logging and OpenTelemetry, or when a missing key does not name its parameter.
- The integration tests give `openai-api-key` a value OpenAI does not accept; they start the application but never call the model. No test needs a real key.
- Every request costs money and sends its content to OpenAI. A use case that sends personal or confidential data to the model needs that decision recorded first.
