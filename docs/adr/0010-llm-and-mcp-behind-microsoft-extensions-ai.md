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

## Consequences

- No profile changes its choices: every profile takes `backend.llm: none`
  and `backend.mcp: []`. `backend.llm.none` has a pack, so every profile that
  generated before still generates; it adds an ADR and agent rules and no
  code.
- `anthropic`, `openai`, `azure-openai`, `ollama`, `server` and `client` have
  no packs yet, so they are `experimental` (ADR-0003), and `fabric new` fails
  with `no pack for ...` for them. Each pack is its own issue, ships its agent
  rules and a gate (ADR-0002), and becomes `supported` once the matrix builds
  it.
- `backend.llm.agents`, retrieval-augmented generation and evaluations are
  later decisions, each with its own ADR.
