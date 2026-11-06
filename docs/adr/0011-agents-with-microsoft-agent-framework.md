# ADR-0011: Agents with Microsoft Agent Framework

**Status:** Accepted
**Date:** 2026-11-05

## Context

ADR-0010 gives a project one `IChatClient` and left agents for later: a use
case can ask the model something, and a chat client can call MCP tools, but a
goal pursued over several steps, with the model choosing which of the
project's own use cases to call, has no place, no rule for what such a tool
may reach, and no gate. ADR-0010 named Microsoft Agent Framework as that later
option, `backend.llm.agents`, built on the provider packs.

The repository owner planned it on 2026-11-01 and made it ready on
2026-11-05 (issue #83): an agent is defined in a module's Application
project, uses that module's use cases as tools through the dispatcher (the
rule MCP tools follow, #81), and is run by a command or query, never directly
by an endpoint; MCP client tools (#82) can be given to an agent. If Agent
Framework were only available as a preview, a preview at least a week old
would do, and the option would stay `experimental` until a stable release.

## Decision

**A project that calls a model chooses whether it builds agents.**

- A new dimension `backend.llm.agents`, project scope, one choice, active
  only when `backend.llm != none`, default `none`:

  | Value | The project |
  |---|---|
  | `none` | calls the model from its use cases directly; no module references an agent framework. |
  | `agent-framework` | builds agents with Microsoft Agent Framework on its `IChatClient`. |

  A project without a model has no such choice: setting it fails, saying
  that it applies only when `backend.llm != none` (docs/catalog-rules.md).

- **The package is `Microsoft.Agents.AI`, pinned at 1.26.0.** It is a stable
  release, published by Microsoft on 2026-10-23; 1.27.0 (2026-10-30) is less
  than a week old. Agent Framework has been stable since 1.0.0
  (2026-04-02), so the owner's preview allowance is not needed. 1.26.0
  depends on `Microsoft.Extensions.AI` >= 10.10.0 and
  `Microsoft.Extensions.AI.Abstractions` >= 10.10.1, which the 10.11.0 the
  base already declares satisfies; it brings `Microsoft.Agents.AI.Abstractions`
  1.26.0 with it. The pack declares the version; no other pack does.

- **Who references what.**
  - Every module's Application project references `Microsoft.Agents.AI`
    (`modules.packages.application`). An agent is a class there that creates
    it from the `IChatClient` and the `IDispatcher` it is given:
    `model.AsAIAgent(instructions: ..., tools: [...])`.
  - The BuildingBlocks gain `DispatcherTool.For<TRequest>(dispatcher,
    description)`, which makes a command or query a tool: named after the
    request in snake case, the record as its input schema, each call read
    into the record and sent through `IDispatcher`, a failed `Result` handed
    to the model as its `ApiError`'s code. It needs only
    `Microsoft.Extensions.AI`'s abstractions, so the BuildingBlocks reference
    no agent framework.
  - **An `Option<T>` argument is required and may be null** (owner's
    decision on #83, 2026-11-05). Its schema is
    `anyOf: [<T's schema>, {"type": "null"}]`, written in the same schema
    pass as the request so its `$ref`s stay valid, and the property is
    listed as required, even when its constructor parameter has a default:
    `null` means None. An omitted argument is refused
    before dispatch, and the model is told which argument is missing.
  - **No `Option<T>` inside a collection or dictionary of a tool request**
    (`Option<Filter>[]`, `Dictionary<string, Option<T>>`, or a class deriving
    from `List<Option<int>>`), by the same
    decision: no schema describes it. `DispatcherTool.Problems` names the
    request and the property, `DispatcherTool.For` refuses such a request,
    and the architecture rule `EveryAgentToolRequestIsValid` fails the build
    on it.
  - **A paged query is a tool too** (owner's decision on #185, built in #215).
    The Kernel's `Page` and `PageSize` are whole numbers in a tool's schema and
    arguments, with their bounds, read through `TryFrom` as a list endpoint
    reads its query string; a value out of bounds is refused before dispatch.
    Arguments the request record cannot be built from are refused the same
    way, the model told why, whether the JSON does not match, a type has no
    constructor JSON can call, or a constructor refuses the value: none of
    them ends the agent's run.
  - With `backend.mcp` containing `client`, the BuildingBlocks also gain
    `IAgentMcpTools`, the declared servers' tools for an agent, each call
    checked by the project's `allow` before it reaches the server, as
    `UseMcpTools` does for a chat client (ADR-0006: a file the pack ships only
    for that choice).
  - The host references nothing new.

- **ADR-0010's first rule holds.** Domain and Application still reference no
  provider SDK, no MCP transport and not `Microsoft.Extensions.AI` itself:
  the rule `ProviderSdksStayOutOfDomainAndApplication` is unchanged, and an
  Application project defining an agent passes it. Agent Framework is the
  orchestration layer above the abstraction, not a provider.

- **The rules a project with agents follows**, each an agent rule of the
  pack, with gates:
  1. **An agent lives in a module's Application project.** The architecture
     rule `AgentsLiveInApplication` fails the build when a module's Domain,
     Infrastructure or Api, or the BuildingBlocks, reference Agent Framework:
     an agent created or run there, an endpoint running one included.
  2. **A command or query runs an agent.** Its handler creates and runs it;
     an endpoint sends the command or query. Rule 1 is what enforces this for
     endpoints.
  3. **An agent's tools go through the dispatcher.** The architecture rule
     `AgentToolsGoThroughTheDispatcher` fails the build when a module makes a
     tool other than with `DispatcherTool.For`: a type of its own deriving
     from `AITool`, `new` on one, or a call to a method returning one that the
     BuildingBlocks do not declare (`AIFunctionFactory.Create`, an agent's
     `AsAIFunction`), found in the compiled code, lambdas and async methods
     included. A call counts as making a tool only when the called method's
     assembly is `Microsoft.Extensions.AI`'s abstractions or references
     `Microsoft.Extensions.AI` (owner's decision on #83): a list's indexer or
     LINQ's `Single` only hands over a tool already made.
  4. **Agents work together through use cases.** An agent that needs another
     calls the command or query that runs it, as a tool; that is the
     multi-agent path this option offers.
  5. **Tests never call a real model** (ADR-0010). A stand-in that asks for a
     tool call, `tests/Fakes/ToolCallingChatClient.cs`, is shipped beside the
     base's `FakeChatClient`, which only answers text; `tests/Agents.Tests`
     runs a sample agent through a query and the dispatcher with it.

- **Both options are `experimental`** (ADR-0003). No profile calls a model,
  so neither is in the profile run or the pairwise matrix; `none` stays
  experimental until a provider does, and `agent-framework` until the matrix
  builds it.

**Not part of this choice:** graph workflows (`Microsoft.Agents.AI.Workflows`,
for sequential, concurrent, handoff and group patterns), hosting agents as
separate services, and human-in-the-loop approval. The workflows package
1.26.0 depends on `OpenTelemetry.Api` 1.18.0 while the base pins OpenTelemetry
1.17.0, so it would move the base's telemetry with it; it is a later decision
of its own.

## Sources checked

`learn.microsoft.com` was not reachable from the session that wrote this ADR
(the proxy refused the connection), nor was the `dotnet/docs` repository
through the GitHub API. These were read on 2026-11-05:

1. NuGet, `Microsoft.Agents.AI` (nuget.org/packages/Microsoft.Agents.AI and
   the registration API): owners Microsoft and MicrosoftAgentFramework,
   reserved prefix, MIT licence; stable 1.0.0 on 2026-04-02, 1.26.0 on
   2026-10-23, 1.27.0 on 2026-10-30, no preview after 1.0.0.
2. The nuspecs of `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Abstractions`
   and `Microsoft.Agents.AI.Workflows` 1.26.0: their dependencies, as above.
3. The XML documentation in `Microsoft.Agents.AI` 1.26.0: `ChatClientAgent`
   "provides an AIAgent that delegates to an IChatClient implementation", and
   `IChatClient.AsAIAgent(instructions, name, description, tools, ...)`
   creates one; tools "are invoked without user approval" by default and
   their arguments "should be treated as untrusted input".
4. `microsoft/agent-framework`, `README.md` and `dotnet/README.md`: "an open,
   multi-language framework for building production-grade AI agents and
   multi-agent workflows in .NET and Python", installed with
   `dotnet add package Microsoft.Agents.AI`, documented at
   learn.microsoft.com/agent-framework.
5. `dotnet/docs`, `docs/ai/dotnet-ai-ecosystem.md` (Learn:
   `dotnet/ai/dotnet-ai-ecosystem`), ms.date 2026-04-15, read raw: Agent
   Framework "is the orchestration layer for systems that are truly agentic:
   they pursue a goal across multiple steps, make decisions along the way,
   use tools, and might coordinate multiple agents"; "MEAI alone isn't an
   agent framework".

## Consequences

- No profile changes: every profile takes `backend.llm: none`, so
  `backend.llm.agents` is inactive and left out of its `fabric.yaml`, and
  every profile generates byte for byte what it did before.
- A project with a model gains `backend.llm.agents: none` in its
  `fabric.yaml`, the `none` pack's ADR and agent rules, and its gate, the
  architecture rule `NoAgentFramework`.
- What the gates cannot see: which command or query runs an agent; a tool
  made by reflection; whether a tool's description says what the use case
  does; whether a command is safe in a model's hands. The agent rules say to
  give an agent only use cases a model may call with arguments of its own
  choosing.
- Every step of an agent is a request to the provider: it costs money and
  sends its content, tool results included, to the provider.
