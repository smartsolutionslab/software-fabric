# Dashboards: generative

## Context

Users of the dashboards want to ask for a widget rather than look for it: "show the widgets per dashboard as a pie". A model that could name any query, or save on the user's behalf, would get around the declared widget catalogue and the user's own say over their dashboard.

## Decision

- **The dashboards module has an agent, `dashboards`**, which the agent panel reaches through `POST /agents/dashboards` (frontend.agent-ui: ag-ui). It runs through the module's query `TalkToTheDashboards`, like every agent.
- **Its tools are two queries, sent through the dispatcher as the caller:** `ListWidgets` (`list_widgets`, the declared widgets the caller may see, a page at a time) and `ProposeWidget` (`propose_widget`).
- **A proposal saves nothing.** `ProposeWidget` takes a declared widget's id, a visual and, optionally, the key of the open draft's widget it replaces. It answers the widget under its declared title, or refuses an undeclared or hidden widget (`dashboards.unknown-widget`), a visual its kind cannot show or a key a dashboard cannot hold (`dashboards.invalid-widget`). The browser shows the proposal pending; the user accepts it into the draft and saves it with `PUT /dashboards/{id}`, through every check a save makes.
- **The open draft is context, not input.** The browser sends the draft's widgets (key, source, visual) as the run's AG-UI state. The query reads it in shape and size only (at most 50 widgets, short texts; otherwise `dashboards.invalid-draft`), gives the model the declared widgets among them with their declared titles in one message before the conversation, and never saves it.
- **The architecture rule `TheDashboardAgentSavesNothing`** fails the build when the dashboards module gives an agent a command as a tool.
{{~ if project["frontend.architecture"] == "spa" && ((project["frontend.framework"] == "react" && project["frontend.api-client"] == "rtk-query-codegen") || (project["frontend.framework"] == "vue" && project["frontend.api-client"] == "orval")) ~}}
{{~ vue = project["frontend.framework"] == "vue" ~}}
- **On the dashboard page**, `DashboardAssistant{{ if vue }}.vue{{ end }}` talks to the agent below the grid and sends the open draft as the run's state, read as each run starts. Each widget it proposes, however many a run brings, shows on the grid as a `PendingWidget{{ if vue }}.vue{{ end }}`, with its live data from `GET /dashboards/widgets/{id}`, until the user accepts or discards it. Accept puts it on the draft, below the other widgets or in the place of the one it replaces; the existing Save persists it. A refused proposal is shown as refused, and nothing is pending.
- **Its tests:** `web/app/src/dashboards/DashboardAssistant.test.{{ if vue }}ts{{ else }}tsx{{ end }}` (the draft sent as state, a proposal pending with its data, Accept, Discard, a replacement, a refusal{{ if array.contains(project["frontend.test.extras"], "accessibility") }}, axe{{ end }}) and `web/app/e2e/generative-dashboards.spec.ts`, against the AppHost with the stand-in model: a proposed chart accepted, saved and found after a reload; an undeclared one refused, with nothing pending or saved.
{{~ end ~}}

## Consequences

- An agent's proposal is as safe as the catalogue: the model reaches only declared widgets the caller may see, and only the user saves.
- The draft the model reads is what the browser says it shows; it only shapes proposals, which are checked again when saved.
