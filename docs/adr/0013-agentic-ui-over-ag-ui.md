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

**As built for the backend (#202).** These follow from the decisions above;
none is a new choice of option.

- **The query.** `IAgentConversation`, in the BuildingBlocks, is
  `IQuery<Result<IAsyncEnumerable<ChatResponseUpdate>, ApiError>>` carrying the
  run's `Messages` and its `State` (`JsonElement?`, null when the browser sent
  none). A module's query implements `IAgentConversation<TSelf>`, whose static
  `AgentName` is the `{name}` of `POST /agents/{name}` and whose static
  `Start(messages, state)` builds one run, so the compiler checks both.
- **Registered by name in the module's `IModule`.** The ag-ui pack fills
  `modules.infrastructure` with `AddModuleAgentConversations<TModule>()`,
  which the module's `IModule.AddServices` calls: every
  `IAgentConversation<TSelf>` in its Application project is registered under
  its name. No shared file lists the agents (ADR-0005). The host refuses to
  start on a name two queries share, or one that is not lower kebab-case,
  naming the queries.
- **What the endpoint passes on.** The run's messages, through the SDK's
  `ToChatRequestContext`, without the browser's system and developer
  messages: an agent's instructions are its module's. Tools the browser
  declares are not given to the agent, so the model reaches data only through
  the module's use cases. `state` is passed through unread. A failed `Result`
  is answered with the same problem details as any endpoint; success with
  `text/event-stream`, written by `AGUI.Formatting`'s
  `SseEventStreamFormatter`. The endpoint is left out of the OpenAPI
  document: the panel's client speaks AG-UI, not the generated REST client.
- **The JSON the SDK needs.** The SDK's type resolver alone cannot write an
  update's tool-call arguments and results, so the host combines it with
  `Microsoft.Extensions.AI`'s (`AIJsonUtilities.DefaultOptions`).
- **The stand-in model** is `src/Host/Agents/StandInChatClient.cs`. The
  AppHost's parameter is `builder.AddParameter("model-stand-in", ...)` with a
  value read from configuration (`Parameters:model-stand-in`, `false` when
  absent): a parameter given a fixed value ignores configuration (Aspire
  13.6.4, checked), so the fixture could not switch it on. The host reads it
  as `Agents:ModelStandIn` and, in Development only, replaces the provider's
  `IChatClient` with the stand-in, which calls the first tool it is offered
  with the user's message and streams its answer a word at a time. The
  `AspireFixture` sets it (`integration-tests.parameters`), and `e2e.sh`
  through the slot the React panel added for it (below). Only a tool result
  after the latest user message counts, so every turn of a conversation calls
  the tool again (#203). A message that is an offered tool's name, a space and a
  JSON object calls that tool with those arguments instead (#185), so a test
  can ask for a call whose arguments are not one text.
- **A run that fails mid-stream** reaches the browser as the SDK's generic
  `RUN_ERROR`. The SDK traces the cause on its `agui.run` span, on the source
  `AGUIServerInstrumentation.ActivitySourceName` (`Experimental.AGUI.Server`),
  and only while something listens. The pack names that source through the
  base's `service-defaults.activity-sources` slot (#205).
- **The gate** `AgentUiGoesThroughAQuery` reads the compiled assemblies: the
  modules and the BuildingBlocks reference no `AGUI.*`, the host (which the
  architecture tests already reference for their endpoint rules) references
  no `Microsoft.Agents.AI*`, and nothing references
  `Microsoft.Agents.AI.Hosting.AGUI*`. It replaces `none`'s `NoAgentUi`.
- **The sample agent** is the `assistant` module's `AssistantAgent`, with the
  module's own `CountWords` query as its tool and `TalkToTheAssistant` as the
  query that runs it; `tests/Modules/Assistant.Tests` runs it through the
  dispatcher with `ToolCallingChatClient`, and `tests/Integration.Tests`
  through the running host.
- The backend generates the same for React and Vue; the panel's templates
  are where the pack branches on the framework (#183, below).

**As built for the React panel (#203).** These follow from the decisions
above too.

- **The panel** is `AgentPanel` in `web/app/src/agents`, with `useAgent` (the
  conversation, kept by `@ag-ui/client`'s `HttpAgent`, so every run sends all
  of it) and `connectAgent` (the `HttpAgent` for `/api/agents/{name}`). Its
  `fetch` adds the signed-in user's token for every run, read as the API
  client reads it (`@/auth/accessToken`); without `backend.auth` it adds
  none. It shows the user's messages, each tool call with its arguments and
  result, and the answer while it streams, all as text. It is built from
  the shadcn `Button` and a textarea styled with the theme, in a React Hook
  Form with a zod schema, as the project's other forms are.
- **Where it shows.** The `assistant` module's page renders it. That page
  is the router pack's, one per module, and there are no per-module slots,
  so `react-router`'s page template branches on this choice (ADR-0006), as
  it does for the dashboards module.
- **Where it is not built yet.** It is built for a React single-page app
  on shadcn/ui, React Hook Form and zod, and since #183 for a Vue one
  (below). With micro-frontends the web app gets
  `src/agents/AgentPanel.test.ts`, which fails its tests saying so; with
  another UI, forms or validation library, the panel's module fails the
  build saying so.
- **`e2e.sh`** has a slot, `e2e.apphost-arguments`, a role slot of
  `frontend.framework` (ADR-0008) joined by a space, so the React and Vue
  packs both declare it (each owns its copy of the script): arguments the script
  starts the AppHost with, after `--`. The pack fills it with
  `--Parameters:model-stand-in=true`. Arguments rather than environment
  variables: Aspire's CLI, which `dotnet run` hands the AppHost to, does not
  pass on a variable whose name has a hyphen, as every `Parameters__<name>`
  does (Aspire 13.6.4, checked). Empty, the script is unchanged. The model
  providers with a key (Anthropic, OpenAI, Azure OpenAI) fill it too,
  optionally, with the key-shaped values they give the `AspireFixture`:
  without them the AppHost waits for its secret parameter and never starts
  the host. Embedding providers and Ollama do not yet; a project's
  end-to-end tests with those wait for them the same way.
- **Lint** refuses `@copilotkit/*` in the web app.
- **Its tests.** `AgentPanel.test.tsx` feeds the panel a server-sent event
  stream the test writes, and checks axe where the project has the
  accessibility tests. `e2e/agent-panel.spec.ts` sends two messages through
  the panel against the AppHost with the stand-in model.
- **Shared state** (#185). `useAgent` takes an optional getter, read as each
  run starts and sent as the run's `state`; the panel passes none. Generative
  dashboards pass the open draft (ADR-0015). Vue's composable takes the same
  getter.

**As built for the Vue panel (#183).** The same panel for a Vue
single-page app, a branch of the same pack (ADR-0006); nothing above
changes for React.

- **The panel** is `AgentPanel.vue` in `web/app/src/agents`, which renders
  `AgentConversation.vue` keyed by the agent's name, so another agent starts
  another conversation, as React's panel does with its `key`. `useAgent` is a
  composable over the same `HttpAgent`, ending the run and its subscription
  with the component's scope. `connectAgent` is the same file for both
  frameworks, so the token is read the same way, from `@/auth/accessToken`.
  It is built from the shadcn-vue `Button` and a textarea styled with the
  theme, in a vee-validate form with a zod schema through the forms pack's
  `toTypedSchema`, as the project's other Vue forms are.
- **Where it shows.** The `assistant` module's page is `vue-router`'s, so
  that pack's page template branches on this choice (ADR-0006), as
  `react-router`'s does.
- **Where it is not built yet.** With another Vue UI library (Reka UI
  alone, Vuetify, ...), forms or validation library, `AgentConversation.vue`
  throws when it loads, saying so, and its imports fail the build.
- **Which files branch.** A target is claimed whatever its `when` says, so a
  file both frameworks have at one path branches inside its template: `useAgent.ts`
  (a hook or a composable), and `src/agents/AgentPanel.test.ts` (Vue's
  component test, or with micro-frontends the test that fails saying the
  panel is not built there).
- **Its tests.** `AgentPanel.test.ts` is `AgentPanel.test.tsx`'s cases
  with Testing Library for Vue; it waits for the Send button to be enabled
  and for a request to be sent rather than for the button to exist, because
  Vue renders a tick after the state changes and vee-validate validates
  before it submits. The end-to-end test is React's file,
  `e2e/agent-panel.spec.ts`: the page's roles and names are the same.

**The one hand-written call to the host (owner's decision on #607,
2026-11-12).** The api-client packs' rule is that the web app never writes
`fetch` to the backend, and the auth-client packs' that only
`src/api/baseApi.ts` sets the `Authorization` header. An AG-UI run is a
server-sent event stream that no generated client models, so `connectAgent`
is a bounded exception rather than a breach:

- `src/agents/connectAgent.ts` alone may call `/api/agents/{name}` and read
  its stream, taking the token from `accessToken()` (`@/auth/accessToken`),
  where `baseApi.ts`'s headers read it. `POST /agents/{name}` stays out of
  the OpenAPI document. Moving it in, with a generated client for the
  request, was the alternative: the stream would still be read by hand.
- The pack's agent rules and ADR state the exception, and its gate is lint,
  through the `frontend.framework` role slot `web.lint-configs` added for it:
  outside `baseApi.ts`, `connectAgent.ts`, the SSE realtime stream's
  `connection.ts` and tests, any reference to `fetch`, `XMLHttpRequest`,
  `Headers`, `HttpAgent` or `EventSource` fails, bare or as a member of
  `window`, `globalThis` or `self`, in a Vue template too. Only a project
  with `frontend.agent-ui: ag-ui` gets the rule; without the panel there is
  no exception to hold.
- Lint bans names, not call shapes (owner's decision on #607, 2026-11-13,
  after three review rounds each found a shape the selectors missed or a
  false positive they made). There is no rule on the `Authorization` header:
  without those names no request carries it. What lint cannot see, a name
  reached through a variable or a computed property, a run-time import, an
  HTTP library not on the list, the generated ADR states; the backend's
  policy is the protection, not lint.

**Delivered in three steps** (owner's decision on #182): first this
dimension, this ADR and the `none` pack; then the `ag-ui` pack's backend; then
its React panel with the end-to-end test. The Vue panel followed as #183, a
branch of the same pack (ADR-0006).

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
