# Agent UI: none

**Status:** Accepted (generated)

## Decision

The project's agents have no panel in the browser. An agent is run by a command or query like any other use case, and a screen that needs one calls that use case through the project's own endpoints and the generated API client. The web app imports no AG-UI or CopilotKit client, and no module speaks the AG-UI protocol.

## Consequences

- Importing `@ag-ui/*` or `@copilotkit/*` fails lint, and a module referencing the AG-UI .NET SDK (`AGUI.*`) or Agent Framework's AG-UI hosting fails the architecture tests.
- An agent panel later is a project-wide choice (`frontend.agent-ui: ag-ui`, with its own ADR), which brings one AG-UI endpoint in the host that runs an agent's query through the dispatcher. A client or an endpoint added by hand skips that rule.
