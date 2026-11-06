# ADR-0010: LLM and MCP behind Microsoft.Extensions.AI

**Status:** Accepted
**Date:** 2026-11-01

## Context

A generated project cannot call a language model or take part in the Model
Context Protocol (MCP): the catalog has no dimension for either, so a project
that needs one adds a provider SDK by hand, in whichever module first needs
it, with no rule for where the key lives, what the tests call, or how far the
model's answer is trusted.

The repository owner decided on 2026-11-01 (issue #76) that a project can use
an LLM and can expose and consume MCP services; the providers are Anthropic,
OpenAI, Azure OpenAI and Ollama; MCP tools are exposed opt-in per command or
query; the abstraction is `Microsoft.Extensions.AI`; Microsoft Agent Framework
comes later as its own option; Semantic Kernel is not used.

## Decision

**A project chooses one LLM provider, or none, and any set of MCP roles.**

- A new dimension `backend.llm`, project scope, one choice, default `none`:

  | Value | The project |
  |---|---|
  | `none` | calls no language model. |
  | `anthropic` | calls Anthropic's models. |
  | `openai` | calls OpenAI's models. |
  | `azure-openai` | calls models deployed in Azure OpenAI. |
  | `ollama` | calls a local model server. |

  One provider per project, chosen at generation: there is no runtime switch
  between providers (ADR-0001). A project that wants another provider
  regenerates that choice.

- A new dimension `backend.mcp`, project scope, `multi: true`, default `[]`:

  | Value | The project |
  |---|---|
  | `[]` | neither exposes nor uses MCP services. |
  | `[server]` | exposes MCP tools. A command or query becomes a tool only by opting in; nothing is exposed by default. |
  | `[client]` | uses external MCP servers. |
  | `[server, client]` | both. |

  As a multi dimension, the two roles combine without a third option for
  "both", as `backend.authorization` does (ADR-0009). It is not tied to
  `backend.llm`: a server exposes tools to other projects' models, and needs
  no model of its own.

**The layering.**

- **`Microsoft.Extensions.AI` is the abstraction.** Every provider pack
  implements, or registers an implementation of, its `IChatClient`; nothing
  outside a provider pack's Infrastructure code names a provider type. The
  .NET documentation describes `IChatClient` as the client abstraction for
  AI services that provide chat, which any .NET LLM client library can
  implement so that consuming code works with it, and names OpenAI, Azure
  OpenAI and Ollama among the providers that integrate with it [1][2].
- **Microsoft Agent Framework is a later option**, `backend.llm.agents`, for
  agents and multi-agent workflows. The documentation separates the two
  layers: `Microsoft.Extensions.AI` is the model-interaction abstraction and
  is not an agent framework by itself; Agent Framework matters when
  orchestration, not model access, is the problem [3]. Any provider usable
  with `Microsoft.Extensions.AI` is usable with Agent Framework [2], so the
  option builds on the provider packs instead of replacing them.
- **Semantic Kernel is not used.** Its repository calls Microsoft Agent
  Framework its successor, and Agent Framework 1.0 is a production release
  [4][5]; `IChatClient` itself was extracted from Semantic Kernel into
  `Microsoft.Extensions.AI` [3]. A new project would start on the line that
  is being left.
- **MCP uses the official C# SDK**: `ModelContextProtocol` (hosting and
  dependency injection) and `ModelContextProtocol.AspNetCore` (HTTP servers),
  with `ModelContextProtocol.Core` for a client that wants the fewest
  dependencies [6]. The SDK is maintained by Microsoft, Anthropic and the MCP
  organisation, and depends on `Microsoft.Extensions.AI` [7], so MCP tools and
  chat clients meet at the same abstraction. No third-party MCP package.

**The rules every LLM and MCP pack follows.** Each is an agent rule in the
pack, and each pack ships a gate for the ones its code can break (ADR-0002).

1. **Domain and Application depend only on `IChatClient`** (and the other
   `Microsoft.Extensions.AI` abstractions), never on a provider SDK or an MCP
   transport.
2. **Provider SDKs live in Infrastructure.** The provider pack registers the
   `IChatClient` there; no other project references the SDK.
3. **API keys are Aspire parameters or secrets** (`AddParameter(..., secret:
   true)` in `src/AppHost`), never configuration files or source.
4. **Tests never call a real model.** A test uses a stand-in `IChatClient`
   that answers what the test needs; a test that would reach a provider or a
   model server is a bug, whatever the network allows.
5. **LLM output is untrusted input.** A model's answer, and anything an
   external MCP server returns, is validated at the boundary like a request
   body: parsed into a typed shape, never executed, interpolated into SQL or
   markup, or used as a tool argument without checking. An exposed MCP tool
   authorises its caller like any endpoint.

## Sources checked

`learn.microsoft.com` was not reachable from the session that wrote this ADR.
The Microsoft Learn pages for .NET AI are published from the `dotnet/docs`
repository, so these were read from their source files there, on 2026-11-01:

1. `dotnet/docs`, `docs/ai/microsoft-extensions-ai.md` (Learn:
   `dotnet/ai/microsoft-extensions-ai`), ms.date 2026-09-14.
2. `dotnet/docs`, `docs/ai/overview.md` (Learn: `dotnet/ai/overview`),
   ms.date 2026-04-15: the provider table and "Any AI provider that's usable
   with Microsoft.Extensions.AI is also usable with Agent Framework".
3. `dotnet/docs`, `docs/ai/dotnet-ai-ecosystem.md` (Learn:
   `dotnet/ai/dotnet-ai-ecosystem`), ms.date 2026-04-15: MEAI versus Agent
   Framework, and the extraction of `IChatClient` from Semantic Kernel.
4. `microsoft/semantic-kernel`, `README.md`: "Microsoft Agent Framework (MAF)
   is the enterprise-ready successor to Semantic Kernel."
5. `microsoft/agent-framework`, `README.md`: a framework for AI agents and
   multi-agent workflows in .NET and Python, with migration guides from
   Semantic Kernel and AutoGen.
6. `modelcontextprotocol/csharp-sdk`, `README.md`: the official C# SDK and
   its three packages.
7. `dotnet/docs`, `docs/ai/get-started-mcp.md` (Learn:
   `dotnet/ai/get-started-mcp`), ms.date 2025-11-20.

Not confirmed by these sources: which package gives Anthropic an
`IChatClient`. Anthropic's C# SDK (`Anthropic`) README does not mention
`Microsoft.Extensions.AI`, and the .NET provider table does not list
Anthropic. The `anthropic` pack's issue settles it; the rule above (one
`IChatClient`, registered in Infrastructure) holds whichever package it is.

**Settled on 2026-11-03 (issue #77):** the official `Anthropic` package,
published by Anthropic, gives Claude an `IChatClient`. Its NuGet package
13.4.0 depends on `Microsoft.Extensions.AI.Abstractions` (>= 10.5.1) and
ships the adapter `AsIChatClient` (namespace `Microsoft.Extensions.AI`),
whose metadata names the provider `anthropic`; read from the package's own
assembly and XML documentation. The repository owner chose it over the
community `Anthropic.SDK`, consistent with "no third-party package" for
MCP, and `claude-haiku-5-5` as the generated default model.

**Settled on 2026-11-04 (issue #78):** Microsoft's
`Microsoft.Extensions.AI.OpenAI` gives OpenAI's models an `IChatClient`,
through its adapter `AsIChatClient` over the official `OpenAI` package
(published by OpenAI). Its NuGet package 10.11.0 depends on
`Microsoft.Extensions.AI.Abstractions` 10.11.0, the version the base's
`Microsoft.Extensions.AI` already brings, and on `OpenAI` 2.14.0, which
comes in transitively and is not pinned; read from the packages' nuspecs.
The repository owner chose it, and `gpt-5-mini` as the generated default
model, with `gpt-5` the stronger alternative.

**Settled on 2026-11-04 (issue #79):** `azure-openai` uses the same
`Microsoft.Extensions.AI.OpenAI` 10.11.0 as `openai`, calling the
resource's OpenAI v1 endpoint (`<endpoint>/openai/v1/`) with the `OpenAI`
package's `OpenAIClientOptions.Endpoint`, so it needs neither
`Azure.AI.OpenAI` (stable 2.1.0 pins an old `OpenAI` 2.1.0; its current
line is beta) nor the preview `Aspire.Azure.AI.OpenAI`. The endpoint is
the Aspire parameter `azure-openai-endpoint` without a default and the
deployment `azure-openai-deployment`, defaulting to `gpt-5-mini` with
`gpt-5` the stronger alternative; the value must match a deployment in
the user's own resource. The resource is not provisioned. The key is the
secret parameter `azure-openai-api-key` and the only authentication:
managed identity would be a runtime switch between two auth paths
(ADR-0001), so it is a later option of its own. The repository owner
accepted all four.

**Settled on 2026-11-04 (issue #80):** `ollama` uses the community
`OllamaSharp` 5.5.0, whose `OllamaApiClient` is an `IChatClient` itself
(provider name `ollama`) and which depends on `Microsoft.Extensions.AI`
>= 10.8.0, so the base's 10.11.0 satisfies it. Microsoft's
`Microsoft.Extensions.AI.Ollama` is deprecated and was never stable; its
NuGet notice recommends `OllamaSharp`. Aspire has no Ollama hosting
integration of its own, so the AppHost uses
`CommunityToolkit.Aspire.Hosting.Ollama` 13.6.1, which depends on the
`Aspire.Hosting` 13.6.4 the base pins: an `ollama/ollama` container at the
tag the package pins (0.34.4), with a data volume, and a model resource
that pulls the model at start, which the host waits for. The default
model is `llama3.2:1b`. The integration-test fixture removes the Ollama
resources and gives the host an endpoint nothing listens on, so no test
pulls the image or a model. Read from the packages' nuspecs and
assemblies; the repository owner accepted all of it.

## Consequences

- No profile changes its choices: every profile takes `backend.llm: none`
  and `backend.mcp: []`. `backend.llm.none` has a pack, so every profile that
  generated before still generates. Its gate is an architecture rule: no
  module references `Microsoft.Extensions.AI` or a provider SDK.
- The MCP SDK depends on `Microsoft.Extensions.AI` [7]. The `server` and
  `client` packs therefore meet that rule when `backend.llm` is `none`; how
  they do (the rule branching on `backend.mcp`, ADR-0006, or MCP code kept
  out of the modules) is settled in their issues, not by weakening the rule
  here. Both keep MCP code out of the modules: `client` creates its clients
  in BuildingBlocks (#82), and `server` puts its `[McpTool]` marker in
  BuildingBlocks, which references no MCP package, and the SDK in the host
  alone (#81).
- `Microsoft.Extensions.AI` has one owner although two dimensions need it:
  the base declares its version and the BuildingBlocks reference when a
  provider is chosen or the MCP client is (owner's decision on #77,
  2026-11-04), and neither the provider packs nor `backend.mcp.client`
  declare it themselves.
- `ModelContextProtocol.AspNetCore` has one owner too: the base declares
  its version whenever `backend.mcp` is not empty (owner's decision on #81,
  2026-11-04). The server references it from the host, the client from the
  tiny server its tests start; `backend.mcp.client` declares only
  `ModelContextProtocol.Core`.
- `anthropic`, `openai`, `azure-openai`, `ollama`, `server` and `client` had
  no packs when this was decided, so they are `experimental` (ADR-0003), and
  `fabric new` fails with `no pack for ...` for an option still without one.
  Each pack is its own issue, ships its agent rules and a gate (ADR-0002), and
  becomes `supported` once the matrix builds it. `client` (#82),
  `server` (#81), `anthropic` (#77), `openai` (#78), `azure-openai` (#79)
  and `ollama` (#80) have packs and stay `experimental` until then.
- **Since 2026-11-06 (#186)** one profile calls a model: `saas-ai-react` extends
  `saas-standard-react` with `openai`, whose key its embeddings reuse, agents, `rag` and
  `evals`, the agent panel and both dashboards (owner's decision on #186). Profile CI uses
  the stand-in model and key-shaped values only, and runs the evals gate with its fake
  judge, never the real eval. Its options stay `experimental` until promoted (ADR-0003).
  The sentences above and in ADR-0011 to ADR-0015 that say every profile takes
  `backend.llm: none` describe the catalog before it.
- `backend.llm.agents` (ADR-0011, 2026-11-05), retrieval-augmented generation (ADR-0012,
  2026-11-05) and evaluations (ADR-0014, 2026-11-05) are later decisions, each with its own ADR.
