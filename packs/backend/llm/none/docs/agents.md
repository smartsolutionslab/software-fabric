## LLM: none

- The project calls no language model. Do not add `Microsoft.Extensions.AI` or a provider SDK to a module; an architecture rule fails the build if one appears.
- A feature that needs a model is a project decision (a `backend.llm` provider, chosen with its own ADR): escalate it with `needs:decision` instead of starting it in one module.
