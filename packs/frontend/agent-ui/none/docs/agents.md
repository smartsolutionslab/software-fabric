## Agent UI: none

- There is no agent panel. Show an agent's answer by calling the command or query that runs it, through the generated API client, like any other use case.
- Do not add an AG-UI or CopilotKit client (`@ag-ui/*`, `@copilotkit/*`) to the web app, or the AG-UI .NET SDK (`AGUI.*`) to a module; lint and an architecture rule fail the build. A panel is a project decision (`frontend.agent-ui: ag-ui`): escalate it with `needs:decision`.
