# LLM: Azure OpenAI

**Status:** Accepted (generated)

## Decision

The project calls models deployed in its own Azure OpenAI resource (Azure AI Foundry), through the resource's OpenAI v1 endpoint.

- **Microsoft's `Microsoft.Extensions.AI.OpenAI` package** supplies the client: its `IChatClient` adapter (`AsIChatClient`) over the official `OpenAI` package (by OpenAI), which it brings with it. The client calls `<endpoint>/openai/v1/`, Azure's OpenAI-compatible API, so neither `Azure.AI.OpenAI` nor a preview package is needed. It is registered once, in `src/BuildingBlocks/Llm/Wiring/AzureOpenAIChatClient.cs`, through the base's `AddModelChatClient`, so logging and OpenTelemetry wrap every call; no other code names either package.
- **The endpoint is a parameter without a default**, `azure-openai-endpoint` (`https://<resource>.openai.azure.com`), which the AppHost hands to the host as `AzureOpenAI__Endpoint`. The project does not provision the resource; it uses one that exists.
- **An endpoint that already ends in `/v1` is used as is**, so the same client runs against any OpenAI-compatible v1 endpoint, OpenAI's own among them. To run locally on a plain OpenAI key, set `azure-openai-endpoint` to `https://api.openai.com/v1`, `azure-openai-api-key` to the OpenAI key and `azure-openai-deployment` to a model name. Any other endpoint gets `/openai/v1/` appended. This is configuration of the one provider this project chose, not a switch between providers: the code, the packages and the parameters stay the same.
- **The deployment is a parameter**, `azure-openai-deployment`, defaulting to `gpt-5-mini`: fast and cheap enough for the questions a generated sample asks. `gpt-5` answers harder questions at a higher price. An Azure deployment's name is whatever its owner typed, so the value must match a deployment in your own resource; changing it changes the model without changing code.
- **The key is an Aspire secret parameter**, `azure-openai-api-key`, handed to the host as `AzureOpenAI__ApiKey`. Locally the endpoint and key come from the AppHost's user secrets (`dotnet user-secrets set Parameters:azure-openai-api-key <key> --project src/AppHost`); in a deployment, from its secret store. The key is the only way to authenticate: managed identity is a later option, not a runtime switch beside the key.
- **Endpoint, deployment and key are read when the client is first needed.** The host starts without them, and the first use fails naming the missing parameter; registering the client sends no request.

## Consequences

- An architecture rule fails when the host's `Program.cs` does not call `AddAzureOpenAIChatClient` (module tests register a stand-in model, so they would not notice), and resolves the registered client, without calling it, failing when it is not the configured deployment at `<endpoint>/openai/v1/`, or at an endpoint ending in `/v1` as given, not wrapped with logging and OpenTelemetry, or when a missing endpoint or key does not name its parameter.
- The integration tests give `azure-openai-endpoint` a resource that does not exist and `azure-openai-api-key` a value Azure does not accept; they start the application but never call the model. No test needs an Azure subscription or a real key.
- Every request costs money and sends its content to your Azure resource. A use case that sends personal or confidential data to the model needs that decision recorded first.
