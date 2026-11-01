## LLM: none

- The project calls no language model. Do not add `Microsoft.Extensions.AI`, a provider SDK or an MCP package by hand.
- A feature that needs a model is a project decision (a `backend.llm` provider, chosen with its own ADR): escalate it with `needs:decision` instead of starting it in one module.
