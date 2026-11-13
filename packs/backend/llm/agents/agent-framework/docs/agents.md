{{~ client = array.contains project["backend.mcp"] "client" ~}}
## LLM agents: Microsoft Agent Framework

- An agent is defined in a module's Application project, under `Agents/`: a static class whose `Create(IChatClient model, IDispatcher dispatcher)` returns `model.AsAIAgent(instructions: ..., name: ..., tools: [...])`. Never create or run an agent in Domain, Infrastructure, Api or BuildingBlocks; an architecture rule fails the build.
- A command or query runs the agent: its handler takes `IChatClient` and `IDispatcher`, creates the agent, runs it and returns its answer's text as a successful `Result`. An endpoint sends that request through `IDispatcher`; it never runs an agent.
- A tool is one of the module's commands or queries: `DispatcherTool.For<FindInvoice>(dispatcher, "what it does, for the model")`. Never `AIFunctionFactory.Create`, an `AITool` of your own or an agent's `AsAIFunction`; an architecture rule fails the build.
- A tool requires of its caller what the endpoint that sends its request requires. Mark that endpoint with `.Sends<FindInvoice>()` where it is mapped (after `MapGet`/`MapPost`), and declare the policy there as for any endpoint; never check access in the agent or the handler. A tool whose request no endpoint marks is refused at every call, and `EveryAgentToolRequestHasAnEndpoint` fails the build.
{{~ if project["backend.auth"] != "none" ~}}
- Run an agent inside the HTTP request that asked for it: outside one (a consumer, a background job) every tool is refused, since there is no signed-in caller to check.
{{~ end ~}}
{{~ if object.has_key(project, "backend.authorization") && array.size(project["backend.authorization"]) > 0 ~}}
- Test a tool's access with `AgentCaller` (`tests/Fakes`): `using var caller = AgentCaller.SignedIn("visitor", AgentCaller.Sending<FindInvoice>(BillingPolicyNames.Manage));`. It registers the policies of every module whose Api project the test project references, as the host does; never register them again.
{{~ else ~}}
- Test a tool's access with `AgentCaller` (`tests/Fakes`): `using var caller = AgentCaller.SignedIn("visitor", AgentCaller.Sending<FindInvoice>());`.
{{~ end ~}}
- Give a tool only to a use case a model may call with arguments of its own choosing. Its handler validates them as it would a request body.
- A list query with `Page` and `PageSize` can be a tool as it is: the model passes them as whole numbers, as `?page=` and `?pageSize=`. Do not add a second, unpaged query for an agent.
{{~ if project["backend.absence"] == "option" ~}}
- An `Option<T>` argument of a tool is required and may be null: the model passes `null` for None, and a call without it is refused before the handler, even when its constructor parameter has a default. Never put an `Option` inside a collection or dictionary of a tool's request; `EveryAgentToolRequestIsValid` fails the build. Use a list of a record whose property is the `Option` instead.
{{~ end ~}}
- To let agents work together, give one agent the command or query that runs the other, as a tool.
{{~ if client ~}}
- To give an agent the declared MCP servers' tools, add `.. await mcpTools.ToolsAsync(allow, cancellationToken)` (`IAgentMcpTools`) to its tools. `allow` checks every call; never pass a check that allows everything for a tool that changes something.
{{~ end ~}}
- An agent's answer, and every tool result it saw, is untrusted input: parse it into a typed shape before acting on it; never execute it or put it into SQL or markup.
- Test an agent with `ToolCallingChatClient` (`tests/Fakes`), which asks for one tool call and answers with its result, as `tests/Agents.Tests` does. No test calls a real model.
