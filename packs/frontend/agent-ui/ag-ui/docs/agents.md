## Agent UI: AG-UI

- The browser talks to agents through one host endpoint, `POST /agents/{name}`. Never add another AG-UI endpoint, in a module or the host, and never reference Agent Framework's AG-UI hosting (`Microsoft.Agents.AI.Hosting.AGUI*`); `AgentUiGoesThroughAQuery` fails the build.
- To offer an agent to the panel, add a query record in the module's Application project under `Queries/<UseCase>/` implementing `IAgentConversation<TSelf>`: `public static string AgentName => "<lower-kebab-name>";` and `public static TSelf Start(IReadOnlyList<ChatMessage> messages, JsonElement? state) => new(messages, state);`. Its handler creates the agent (`Agents/`), runs it with `RunStreamingAsync(query.Messages, ...)` and returns `.AsChatResponseUpdatesAsync()` as a successful `Result`. Nothing else registers it.
- Agent names are unique across modules; the host refuses to start on a duplicate.
- `state` is the panel's, untrusted input: validate it before a tool or the model sees it.
- Test an agent's query with `ToolCallingChatClient` (`tests/Fakes`), as `tests/Modules/Assistant.Tests` does. The integration tests answer through the host's stand-in model, switched on by the AppHost parameter `model-stand-in`; never set it outside tests, and never call a real model from a test.
