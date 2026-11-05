# ADR-0013: Agentic UI over AG-UI

**Status:** Accepted
**Date:** 2026-11-05

## Context

ADR-0011 lets a project build agents with Microsoft Agent Framework. Each agent
is defined in a module's Application project, run by a command or query, and
calls the module's use cases through the dispatcher. Nothing lets a user talk
to an agent from the browser. A screen can send the query that runs an agent
and show its answer once the agent has finished. It sees nothing while the
agent works: no streamed text, and no tool calls as they happen.

The repository owner decided on 2026-11-05, for agentic UI, chat with your
data and dashboards (issues #182 to #186):

- The frontend talks to the backend agents over the **AG-UI protocol**, which
  carries streamed messages, tool calls and shared state.
- The model reaches data **only through the modules' use cases**: dispatcher
  tools (ADR-0011), and later retrieval (ADR-0012). There is no direct or
  text-to-SQL access, and authorization and tenancy apply as they do for any
  request.

The choices that implement this were escalated on #182 and decided by the
owner on 2026-11-05. Each recommendation was accepted
([comment](https://github.com/smartsolutionslab/software-fabric/issues/182#issuecomment-6664365004)).

## Decision

**A project with agents chooses whether its users get an agent panel.**

- A new dimension `frontend.agent-ui`, project scope, one choice, active only
  when `backend.llm.agents == agent-framework`, default `none`:

  | Value | The project |
  |---|---|
  | `none` | has no agent panel. A screen reaches an agent through the command or query that runs it, like any other use case. |
  | `ag-ui` | has an agent panel in the web app, which streams an agent's text and tool calls over AG-UI from one host endpoint. |

  `active-when` rather than `requires`: a project without agents has no such
  choice and its `fabric.yaml` does not name it, so every existing profile
  generates what it did before. An inactive `backend.llm.agents` never
  satisfies `==` (docs/catalog-rules.md), and setting the dimension without
  agents fails, saying that it applies only when
  `backend.llm.agents == agent-framework`.

- **`none`'s rule has gates.** Importing `@ag-ui/*` or `@copilotkit/*` in the
  web app fails lint (`web.restricted-imports`). The architecture rule
  `NoAgentUi` fails the build when a module references the AG-UI .NET SDK
  (`AGUI.*`) or Agent Framework's AG-UI hosting
  (`Microsoft.Agents.AI.Hosting.AGUI*`).

- **`ag-ui`, as decided for its pack:**
  - **The server is a mapping of our own on the official AG-UI .NET SDK**:
    `AGUI.Abstractions`, `AGUI.Server` and `AGUI.Formatting` 1.0.1. They are
    stable (2026-10-12), published by the AG-UI protocol's maintainers under
    the MIT licence, and depend only on `Microsoft.Extensions.AI.Abstractions`
    >= 10.6.0, which the base's 10.11.0 satisfies, and on
    `System.Net.ServerSentEvents`. Agent Framework's own
    `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` is **not** used, for three
    reasons:
    - It is preview only; the newest version that is a week old is
      `1.26.0-preview.261023.1`.
    - Its `MapAGUIServer(pattern, AIAgent)` makes the endpoint run the agent
      itself, against ADR-0011's rule that a command or query runs an agent.
    - It brings `OpenTelemetry.Api` 1.18.0, while the base pins 1.17.0.
  - **One endpoint, `POST /agents/{name}`, in the host**, added through
    `host.services` and `host.endpoints` as the SignalR hub is (#155). It
    requires sign-in unless `backend.auth: none`. It reads the AG-UI
    `RunAgentInput` and sends the named agent's `IAgentConversation` query
    through `IDispatcher`. The handler runs the agent and returns its updates
    as a stream of `ChatResponseUpdate`s. The endpoint turns that stream into
    AG-UI events and writes them as server-sent events. A module registers
    its agent's query by name, by convention (ADR-0005). The architecture rule
    `AgentUiGoesThroughAQuery` fails the build on an AG-UI endpoint that runs
    an agent another way.
  - **The client is `@ag-ui/client` 1.0.3** (2026-10-16), and the panel is
    built from the project's UI components. CopilotKit is not used: its tree
    brings a GraphQL runtime client, a licence verifier, telemetry packages
    and a second charts library. The client posts with `fetch`, so the access
    token travels in the `Authorization` header, as the API client sends it.
  - **Tests never call a real model** (ADR-0010). A stand-in model that
    streams its answer in several chunks and asks for a tool call is switched
    on in Development only, by an AppHost parameter that only the end-to-end
    script and the `AspireFixture` set.
  - **The pack brings an `assistant` module** (#187, pack-declared modules),
    holding a sample agent, the query that runs it and a use case as its tool.
    This gives the panel and its end-to-end test an agent in every generated
    project.
  - Shared state is passed through to the query but not used yet. Generative
    dashboards (#185) are its first consumer.

- **Both options are `experimental`** (ADR-0003). No profile has agents, so
  the dimension is in neither the profile run nor the pairwise matrix.
  `ag-ui` has no pack yet, and `fabric new` fails with `no pack for ...` until
  it has one.

**Delivered in three steps** (owner's decision on #182): first this
dimension, this ADR and the `none` pack; then the `ag-ui` pack's backend; then
its React panel with the end-to-end test. The Vue panel is #183, a branch of
the same pack (ADR-0006). Until #183 lands, the pack's templates fail loudly
for Vue.

## Sources checked

Read on 2026-11-05:

1. nuget.org, `AGUI.Abstractions`, `AGUI.Server` and `AGUI.Formatting`: the
   versions, publish dates and authors (registration API), and the nuspecs'
   dependencies. The `AGUI.Server` README describes the SDK as "a
   framework-agnostic server adapter built around `Microsoft.Extensions.AI`",
   with "server-side adapter from `ChatResponseUpdate` streams to AG-UI
   events". The API, from the XML documentation:
   `RunAgentInput.ToChatRequestContext(...)` and
   `IAsyncEnumerable<ChatResponseUpdate>.AsAGUIEventStreamAsync(...)`.
2. nuget.org, `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`: every version is
   a preview. The nuspec of `1.26.0-preview.261023.1` (dependencies as above)
   and its XML documentation (`MapAGUIServer` overloads taking an `AIAgent`
   or an agent selector).
3. npm, `@ag-ui/client` and `@ag-ui/core`: versions, publish dates,
   dependencies, the `zod` peer dependency and the licence.
4. npm, `@copilotkit/react-core` 1.83.0 and `@copilotkit/shared` 1.83.0:
   dependencies and unpacked size.

## Consequences

- No profile changes. Every profile takes `backend.llm: none`, so
  `backend.llm.agents` is inactive, `frontend.agent-ui` is inactive too, and
  neither appears in its `fabric.yaml`.
- A project with agents gains `frontend.agent-ui: none` in its `fabric.yaml`,
  the `none` pack's ADR and agent rules, the lint restriction and `NoAgentUi`.
- What `none`'s gates cannot see: the host, which the architecture tests do
  not load, and a client written by hand against the AG-UI wire format
  without either library.
