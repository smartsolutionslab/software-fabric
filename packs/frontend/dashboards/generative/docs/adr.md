# Dashboards: generative

## Context

Users of the dashboards want to ask for a widget rather than look for it: "show the widgets per dashboard as a pie". A model that could name any query, or save on the user's behalf, would get around the declared widget catalogue and the user's own say over their dashboard.

## Decision

- **The dashboards module has an agent, `dashboards`**, which the agent panel reaches through `POST /agents/dashboards` (frontend.agent-ui: ag-ui). It runs through the module's query `TalkToTheDashboards`, like every agent.
- **Its tools are two queries, sent through the dispatcher as the caller:** `ListWidgets` (`list_widgets`, the declared widgets the caller may see, a page at a time) and `ProposeWidget` (`propose_widget`).
- **A proposal saves nothing.** `ProposeWidget` takes a declared widget's id, a visual and, optionally, the key of the open draft's widget it replaces. It answers the widget under its declared title, or refuses an undeclared or hidden widget (`dashboards.unknown-widget`), a visual its kind cannot show or a key a dashboard cannot hold (`dashboards.invalid-widget`). The browser shows the proposal pending; the user accepts it into the draft and saves it with `PUT /dashboards/{id}`, through every check a save makes.
- **The open draft is context, not input.** The browser sends the draft's widgets (key, source, visual) as the run's AG-UI state. The query reads it in shape and size only (at most 50 widgets, short texts; otherwise `dashboards.invalid-draft`), gives the model the declared widgets among them with their declared titles in one message before the conversation, and never saves it.
- **The architecture rule `TheDashboardAgentSavesNothing`** fails the build when the dashboards module gives an agent a command as a tool.

## Consequences

- An agent's proposal is as safe as the catalogue: the model reaches only declared widgets the caller may see, and only the user saves.
- The draft the model reads is what the browser says it shows; it only shapes proposals, which are checked again when saved.
