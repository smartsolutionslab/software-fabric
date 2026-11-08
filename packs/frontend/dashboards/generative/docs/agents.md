## Generative dashboards

- The `dashboards` module's agent (`Application/Agents/DashboardAgent.cs`, reached as `POST /agents/dashboards`) proposes widgets; the user accepts and saves them. Never give it, or any agent of the dashboards module, a command as a tool: `TheDashboardAgentSavesNothing` fails the build.
- Its tools are `ListWidgets` and `ProposeWidget`, through `DispatcherTool.For`. A new kind of proposal is another query beside them, checked against `WidgetCatalog` and `IWidgetAccess` as `ProposeWidgetHandler` checks, never a query naming something outside the catalogue.
- The run's state is the browser's open draft, untrusted: read it only through `DashboardDraft.Read`, which bounds its shape and size, and never save it or put it in the agent's instructions.
{{~ if project["frontend.architecture"] == "spa" && ((project["frontend.framework"] == "react" && project["frontend.api-client"] == "rtk-query-codegen") || (project["frontend.framework"] == "vue" && project["frontend.api-client"] == "orval" && (!object.has_key(project, "frontend.state") || project["frontend.state"] != "pinia"))) ~}}
{{~ vue = project["frontend.framework"] == "vue" ~}}
- On the dashboard page, `DashboardAssistant{{ if vue }}.vue{{ end }}` (`web/app/src/dashboards`) is the conversation with the agent; it sends the open draft through `useAgent`'s state getter (`draftState`) and hands each `propose_widget` result to `DashboardCanvas{{ if vue }}.vue{{ end }}`, which shows it on the grid as a `PendingWidget{{ if vue }}.vue{{ end }}`. Accept only changes the draft (`acceptProposal`) and Save persists it: never save from a proposal, and never trust a tool result without `readProposal`.
{{~ end ~}}
