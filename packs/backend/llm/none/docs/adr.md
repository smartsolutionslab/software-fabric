# LLM: none

**Status:** Accepted (generated)

## Decision

The project calls no language model. It references no `Microsoft.Extensions.AI` client, no provider SDK and no MCP package, and needs no API key.

## Consequences

- Using a model later is a project-wide change: a `backend.llm` provider chosen with its own ADR, which brings the `IChatClient` registration, the key as an Aspire secret and the tests' stand-in model. A provider package added to one module by hand skips all three.
