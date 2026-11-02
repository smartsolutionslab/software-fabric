# LLM: none

**Status:** Accepted (generated)

## Decision

The project calls no language model. No module references `Microsoft.Extensions.AI` or a provider SDK, and the project needs no model API key. Whether it exposes or uses MCP services is a separate choice (`backend.mcp`).

## Consequences

- An architecture rule fails the build when a module references `Microsoft.Extensions.AI` or a provider SDK (OpenAI, Azure.AI.OpenAI, Anthropic, OllamaSharp).
- Using a model later is a project-wide change: a `backend.llm` provider chosen with its own ADR, which brings the `IChatClient` registration, the key as an Aspire secret and the tests' stand-in model. A provider package added to one module by hand skips all three.
