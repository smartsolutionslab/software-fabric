## LLM agents: none

- Use cases call the model through `IChatClient` directly. Do not add Microsoft Agent Framework (`Microsoft.Agents.AI`) to a module; an architecture rule fails the build if one appears.
- A feature that needs an agent (a goal pursued over several steps, with tools) is a project decision (`backend.llm.agents: agent-framework`, chosen with its own ADR): escalate it with `needs:decision` instead of starting it in one module.
