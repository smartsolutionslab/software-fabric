{{~ client = array.contains project["backend.mcp"] "client" ~}}
# LLM agents: Microsoft Agent Framework

**Status:** Accepted (generated)

## Decision

The project builds agents with Microsoft Agent Framework (`Microsoft.Agents.AI`, by Microsoft) on its one `IChatClient`, whichever provider the project chose: an agent pursues a goal over several steps, calling tools the model chooses, until it can answer.

- **An agent lives in a module's Application project**, as a class that creates it (`model.AsAIAgent(instructions: ..., tools: [...])`) from the `IChatClient` and the `IDispatcher` it is given. The Application projects reference the framework; no other project does.
- **A command or query runs an agent.** Its handler creates the agent and runs it; an endpoint, a consumer or a background job sends that command or query through `IDispatcher` and never runs an agent itself.
- **An agent's tools are its module's use cases**, made with `DispatcherTool.For<TRequest>(dispatcher, "description")` (BuildingBlocks): the tool is named after the request in snake case (`find_invoice`), its input schema is the request record's, and a call reads the model's arguments into the record and sends it through `IDispatcher`, exactly as an endpoint does. A successful `Result` reaches the model as JSON, a failed one as text starting with the `ApiError`'s code; arguments that do not fit the record never reach the handler: the model is told why, whether the JSON does not match, a type in the record has no constructor JSON can call, or a constructor refuses the value.
- **A paged query can be a tool.** The Kernel's `Page` and `PageSize` are whole numbers in a tool's schema and arguments (`{"type": "integer", "minimum": 1, "maximum": ...}`), read through their `TryFrom` as a list endpoint reads `?page=` and `?pageSize=`; a value out of bounds is refused before dispatch, naming the bounds.
{{~ if project["backend.absence"] == "option" ~}}
- **An `Option<T>` argument is required and may be null.** Its schema is `anyOf: [<T's schema>, {"type": "null"}]`; `null` means None, even when the constructor parameter has a default, and a call that leaves the argument out is refused before dispatch, telling the model which argument is missing. A request with an `Option` inside a collection or dictionary (`IReadOnlyList<Option<int>>`, `Dictionary<string, Option<T>>`) cannot be a tool: no schema describes it, so `DispatcherTool.For` refuses it and the architecture rule `EveryAgentToolRequestIsValid` fails the build, naming the request and the property.
{{~ end ~}}
- **Several agents work together through use cases.** An agent that needs another calls the command or query that runs it, as a tool; there is no agent-as-a-tool shortcut around the dispatcher. Graph workflows (`Microsoft.Agents.AI.Workflows`) are not part of this choice.
{{~ if client ~}}
- **An agent can be given the declared MCP servers' tools**, from `IAgentMcpTools.ToolsAsync(allow, ...)` (BuildingBlocks), next to its own use cases. Every call the model asks for is checked by `allow` before it reaches the server, as with `UseMcpTools`.
{{~ end ~}}
- **Tests never call a real model.** `tests/Fakes/ToolCallingChatClient.cs` stands in for it and asks for a tool call; `tests/Agents.Tests` runs a sample agent through a query and the dispatcher with it.

## Consequences

- An architecture rule fails the build when a module's Domain, Infrastructure or Api, or the BuildingBlocks, reference Microsoft Agent Framework: an agent created or run there, an endpoint's included. It cannot see which command or query runs an agent.
- A second rule fails the build when a module makes a tool other than with `DispatcherTool.For`: a type of its own deriving from `AITool`, `new` on one, or a method returning one, such as `AIFunctionFactory.Create` or an agent's `AsAIFunction`. It cannot see a tool made by reflection, nor whether a tool's description says what the use case really does.
- The model chooses the tools and their arguments: they are untrusted input. Give an agent only use cases a model may call with arguments of its own choosing; a command that changes something on someone's behalf needs its handler's own checks, as for any request.
- An agent's answer is the model's output, untrusted input like any other: parse it into a typed shape before acting on it, and never execute it or put it into SQL or markup.
- Every step of an agent is a model request: it costs money and sends its content, the tools' results included, to the provider.
