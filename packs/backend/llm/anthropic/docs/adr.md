# LLM: Anthropic

**Status:** Accepted (generated)

## Decision

The project calls Claude through the Anthropic API.

- **The official `Anthropic` package** (by Anthropic) supplies the client, through its `IChatClient` adapter (`AsIChatClient`). It is registered once, in `src/BuildingBlocks/Llm/Wiring/AnthropicChatClient.cs`, through the base's `AddModelChatClient`, so logging and OpenTelemetry wrap every call; no other code names the package.
- **The key is an Aspire secret parameter**, `anthropic-api-key`, which the AppHost hands to the host as `Anthropic__ApiKey`. Locally it comes from the AppHost's user secrets (`dotnet user-secrets set Parameters:anthropic-api-key <key> --project src/AppHost`); in a deployment, from its secret store.
- **The model is a parameter**, `anthropic-model`, defaulting to `claude-haiku-5-5`: fast and cheap enough for the questions a generated sample asks. `claude-sonnet-5-5` and `claude-opus-5-5` answer harder questions at a higher price; changing the parameter's value changes the model without changing code.
- **Key and model are read when the client is first needed.** The host starts without them, and the first use fails naming the missing parameter; registering the client sends no request.

## Consequences

- An architecture rule resolves the registered client, without calling it, and fails when it is not Claude with the configured model, not wrapped with logging and OpenTelemetry, or when a missing key does not name its parameter.
- The integration tests give `anthropic-api-key` a value Anthropic does not accept; they start the application but never call the model. No test needs a real key.
- Every request costs money and sends its content to Anthropic. A use case that sends personal or confidential data to the model needs that decision recorded first.
