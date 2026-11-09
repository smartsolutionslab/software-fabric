{{~ signed_in = project["backend.auth"] != "none" ~}}
{{~ vue = project["frontend.framework"] == "vue" ~}}
{{~ panel = (project["frontend.framework"] == "react" || vue) && project["frontend.architecture"] == "spa" ~}}
# Agent UI: AG-UI

**Status:** Accepted (generated)

## Decision

Users talk to the modules' agents from the browser over the AG-UI protocol. The host has one endpoint for it, `POST /agents/{name}` (`src/Host/Wiring/AgentUiEndpoints.cs`), {{ if signed_in }}for signed-in users, like every other route{{ else }}open to anyone, like every other route (`backend.auth: none`){{ end }}. It reads the AG-UI run input, turns its messages into chat messages with the official AG-UI .NET SDK (`AGUI.Server`), sends the named agent's query through `IDispatcher` and writes the agent's updates back as AG-UI events (`TEXT_MESSAGE_*`, `TOOL_CALL_*`, `TOOL_CALL_RESULT`) over server-sent events (`AGUI.Formatting`). It never runs an agent: the query's handler does, in its module's Application project, and the agent's tools are the module's use cases, sent through the dispatcher. The browser's system and developer messages, and any tools it declares, are not passed on; the run's `state` is, untouched and unread so far.

A module offers an agent by implementing `IAgentConversation<TSelf>` (`src/BuildingBlocks/Agents`) with a query record in its Application project: `AgentName` is the `{name}` the browser posts to, and `Start(messages, state)` builds one run. The module registers it by that name when it adds its services, so no shared file lists the agents; the host refuses to start on a name two queries share, naming both. The query's handler runs the agent with `RunStreamingAsync` and returns the updates, as a successful `Result`, as they come.

The `assistant` module is the sample: `AssistantAgent` counts words with the module's own `CountWords` query as its tool, and `TalkToTheAssistant` runs it.

{{ if panel }}The web app's agent panel, `AgentPanel{{ if vue }}.vue{{ end }}` (`web/app/src/agents`), is the `assistant` module's page. It talks to the endpoint with `@ag-ui/client`'s `HttpAgent` through `/api`, like every other call, {{ if signed_in }}with the signed-in user's access token in the `Authorization` header, read for every run as the API client reads it, {{ end }}and sends the whole conversation each time. It shows the user's messages, each tool call with its arguments and result, and the agent's text while it streams in, all as plain text; it is built from the project's {{ if vue }}{{ if project["frontend.ui-library"] == "reka-ui" }}Reka UI `BaseButton`{{ else }}shadcn-vue components{{ end }} and {{ if project["frontend.forms"] == "tanstack-form" }}TanStack Form{{ else if project["frontend.forms"] == "formkit" }}FormKit{{ else }}vee-validate{{ end }}{{ else }}shadcn/ui components and {{ if project["frontend.forms"] == "tanstack-form" }}TanStack Form{{ else }}React Hook Form{{ end }}{{ end }}. CopilotKit is not used: its client brings a GraphQL runtime, a licence verifier and telemetry the panel does not need. A page that shows the agent what is on screen passes `useAgent` a getter, read as each run starts and sent as the run's `state`.{{ else }}The agent panel is built for a React or Vue single-page app only so far; here (`frontend.framework: {{ project["frontend.framework"] }}`, `frontend.architecture: {{ project["frontend.architecture"] }}`) `web/app/src/agents/AgentPanel.test.ts` fails the web app's tests saying so, until the generator builds it for this combination.{{ end }}

Tests never call a model. In Development, the AppHost parameter `model-stand-in` set to `true` makes the host answer every agent with `StandInChatClient` (`src/Host/Agents`), which calls the first tool it is offered for the latest message, or the offered tool a message names followed by a JSON object of its arguments (`propose_widget {"source": ...}`), and streams its answer a word at a time. Only the integration tests and the end-to-end script (`.github/scripts/e2e.sh`) set the parameter; it is `false` otherwise, and the host ignores it outside Development.

## Gates

- `AgentUiGoesThroughAQuery` (architecture tests): no module and not the BuildingBlocks reference the AG-UI SDK (`AGUI.*`), the host references no Agent Framework package, and nothing references Agent Framework's AG-UI hosting (`Microsoft.Agents.AI.Hosting.AGUI*`), whose endpoint runs an agent itself.
- `tests/Integration.Tests/AgentUiTests.cs`, against the running application: the assistant's answer arrives in more than one `TEXT_MESSAGE_CONTENT` event after a `count_words` tool call whose result its handler computed{{ if signed_in }}; an anonymous caller is refused{{ end }}; a second turn calls the tool again for its own message; an agent no module registers is not found.
- `tests/Modules/Assistant.Tests/Queries/TalkToTheAssistantTests.cs`: the assistant's query streams a tool call through the dispatcher and the answer built from its result.
{{~ if panel ~}}
- Lint: importing CopilotKit (`@copilotkit/*`) fails.
- `web/app/src/agents/AgentPanel.test.{{ if vue }}ts{{ else }}tsx{{ end }}`: the panel shows the answer while it streams, after the tool call and its result, sends the whole conversation{{ if signed_in }} with the user's token{{ end }}, shows a refused run or a streamed `RUN_ERROR` as an alert, and starts a new conversation for another agent{{ if array.contains(project["frontend.test.extras"], "accessibility") }}; axe finds no violations{{ end }}.
- `web/app/e2e/agent-panel.spec.ts`, against the AppHost with the stand-in model: two messages from the assistant's page, each answered in several streamed chunks after a `count_words` call whose result the panel shows.
{{~ end ~}}

## Consequences

- Every message a user sends is a request to the provider, tool results included, with its cost; a conversation's history travels with every run, from the browser.
- A run that fails after its stream has started reaches the browser only as a generic `RUN_ERROR`, so no exception text leaks to it. The cause is in the trace: ServiceDefaults listen to the SDK's source (`Experimental.AGUI.Server`), whose `agui.run` span ends with an error status, the exception's message and its type (`error.type`).
- An agent's answer is untrusted input: the panel shows it as text and never executes it.
- What the gate cannot see: an endpoint that speaks the AG-UI wire format by hand without the SDK, and which use cases an agent is given. Give an agent only the use cases its users may call.
