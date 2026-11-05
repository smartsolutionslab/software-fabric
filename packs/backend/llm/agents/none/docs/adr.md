# LLM agents: none

**Status:** Accepted (generated)

## Decision

The project's use cases call the model directly, through `IChatClient`: one request, one answer, and at most the tool calls a use case wires itself. There is no agent framework, and no module references Microsoft Agent Framework.

## Consequences

- An architecture rule fails the build when a module references Microsoft Agent Framework (`Microsoft.Agents.AI*`).
- Agents later are a project-wide choice (`backend.llm.agents: agent-framework`, with its own ADR), which brings the rules for where an agent lives, what runs it and how its tools reach the module's use cases. A package added to one module by hand skips all three.
