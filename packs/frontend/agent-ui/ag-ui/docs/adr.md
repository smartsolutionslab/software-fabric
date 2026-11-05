{{~ signed_in = project["backend.auth"] != "none" ~}}
# Agent UI: AG-UI

**Status:** Accepted (generated)

## Decision

Users talk to the modules' agents from the browser over the AG-UI protocol. The host has one endpoint for it, `POST /agents/{name}` (`src/Host/Wiring/AgentUiEndpoints.cs`), {{ if signed_in }}for signed-in users, like every other route{{ else }}open to anyone, like every other route (`backend.auth: none`){{ end }}. It reads the AG-UI run input, turns its messages into chat messages with the official AG-UI .NET SDK (`AGUI.Server`), sends the named agent's query through `IDispatcher` and writes the agent's updates back as AG-UI events (`TEXT_MESSAGE_*`, `TOOL_CALL_*`, `TOOL_CALL_RESULT`) over server-sent events (`AGUI.Formatting`). It never runs an agent: the query's handler does, in its module's Application project, and the agent's tools are the module's use cases, sent through the dispatcher. The browser's system and developer messages, and any tools it declares, are not passed on; the run's `state` is, untouched and unread so far.

A module offers an agent by implementing `IAgentConversation<TSelf>` (`src/BuildingBlocks/Agents`) with a query record in its Application project: `AgentName` is the `{name}` the browser posts to, and `Start(messages, state)` builds one run. The module registers it by that name when it adds its services, so no shared file lists the agents; the host refuses to start on a name two queries share, naming both. The query's handler runs the agent with `RunStreamingAsync` and returns the updates, as a successful `Result`, as they come.

The `assistant` module is the sample: `AssistantAgent` counts words with the module's own `CountWords` query as its tool, and `TalkToTheAssistant` runs it.

Tests never call a model. In Development, the AppHost parameter `model-stand-in` set to `true` makes the host answer every agent with `StandInChatClient` (`src/Host/Agents`), which calls the first tool it is offered and streams its answer a word at a time. Only the integration tests set the parameter; it is `false` otherwise, and the host ignores it outside Development.

## Gates

- `AgentUiGoesThroughAQuery` (architecture tests): no module and not the BuildingBlocks reference the AG-UI SDK (`AGUI.*`), the host references no Agent Framework package, and nothing references Agent Framework's AG-UI hosting (`Microsoft.Agents.AI.Hosting.AGUI*`), whose endpoint runs an agent itself.
- `tests/Integration.Tests/AgentUiTests.cs`, against the running application: the assistant's answer arrives in more than one `TEXT_MESSAGE_CONTENT` event after a `count_words` tool call whose result its handler computed{{ if signed_in }}; an anonymous caller is refused{{ end }}; an agent no module registers is not found.
- `tests/Modules/Assistant.Tests/Queries/TalkToTheAssistantTests.cs`: the assistant's query streams a tool call through the dispatcher and the answer built from its result.

## Consequences

- Every message a user sends is a request to the provider, tool results included, with its cost; a conversation's history travels with every run, from the browser.
- An agent's answer is untrusted input: the panel shows it as text and never executes it.
- What the gate cannot see: an endpoint that speaks the AG-UI wire format by hand without the SDK, and which use cases an agent is given. Give an agent only the use cases its users may call.
