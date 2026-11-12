# ADR-0015: User-editable dashboards from declared widgets

**Status:** Accepted
**Date:** 2026-11-05

## Context

The repository owner decided on 2026-11-05, for agentic UI, chat with your data
and dashboards (issues #182–#186), that a generated project can offer
**user-editable** dashboards, where users arrange and save widgets per user and
tenant, and **generative** dashboards, where an agent creates or changes widgets
on request. A widget shows what a module's query answers, as an ECharts chart or
a DataTable.

A dashboard that could name any query would be a way around every endpoint's
authorization: the model in #185 and a user editing JSON alike could reach
whatever the dispatcher can. And a dashboard is state of its own, saved per user,
so it needs a home that follows the module, CQRS, tenancy and retry rules of
every other module.

The owner settled the open questions on #184 (comment of 2026-11-05) by
accepting the recommendations put to them: the grid library, the catalog shape,
a typed declaration per module, and a module the pack brings (ADR-0005, #187).

## Decision

**A project chooses which kinds of dashboard it offers, if any.**

- A new dimension `frontend.dashboards`, project scope, `multi: true`,
  `active-when: backend.auth != none`, default `[]`:

  | Value | The project |
  |---|---|
  | `[]` | has no dashboards. |
  | `[user-editable]` | lets each user arrange, resize and save widgets on dashboards of their own. |
  | `[user-editable, generative]` | also lets an agent create or change widgets on request (#185). |

  `generative` requires `user-editable` and, since #185, `frontend.agent-ui ==
  ag-ui` (which is active only with `backend.llm.agents == agent-framework`):
  the agent is reached through the agent panel's endpoint. As a multi dimension the two combine without a third option,
  as `backend.authorization` and `backend.mcp` do (ADR-0009, ADR-0010). A
  dashboard belongs to a signed-in user, so without authentication the dimension
  is inactive.
- `user-editable` also requires `backend.errors == result`,
  `backend.data-access == ef-core` and `backend.database == postgres`: its module's
  handlers, store and migration are written for those, and every other option of
  those dimensions is still without a pack (ADR-0006 rule 2: say so, never render
  something that does not build).

**Widgets are declared, typed, per module.** A module implements
`IModuleWidgets` (`src/BuildingBlocks/Dashboards`) once, in its Application
project, like `IModulePolicies`:

```csharp
public sealed class BillingWidgets : IModuleWidgets
{
    public void Declare(WidgetCatalogBuilder widgets) => widgets
        .Chart("monthly-revenue", "Monthly revenue", () => new MonthlyRevenue())
        .Table<OpenInvoices, OpenInvoiceRow>("open-invoices", "Open invoices", (page, pageSize) => new OpenInvoices(page, pageSize));
}
```

- A chart's query answers `Result<ChartSeries, ApiError>` (categories plus named
  numeric series, which ECharts takes as they are); a table's answers
  `Result<PagedResult<TRow>, ApiError>`, which `DataTable` takes. The generic
  constraints make that a compile-time check.
- A widget's query takes no arguments but the page to read, built by the
  declaration, so neither a user nor a model chooses its input.
- A widget's id is `<module>.<key>`. The host builds one `WidgetCatalog` from
  the modules' declarations, and nothing outside it can be named or run.
- With authorization choices a widget may name one of its module's own
  policies. A widget's query runs through the dispatcher, not through the
  endpoint that may guard the same query, so the widget carries the policy:
  the catalogue lists, and runs, only the widgets whose policy the caller holds.
- `WidgetCatalog.Scan` is the one definition of a valid widget. The widget's
  query is a query of the declaring module's Application project with exactly
  one handler, ids are unique, and a named policy is one the module registers.
  The host refuses to start on a problem, and the architecture rule
  `EveryWidgetIsADeclaredModuleQuery` fails the build on the same problems, and
  on an `IModuleWidgets` outside an Application project.

The owner chose this over an attribute on the query (`[Widget]`, like
`[McpTool]`), whose result type only an architecture rule could check.

**The dashboards live in a module the pack brings** (ADR-0005): `dashboards`,
with `module.cqrs: logical` pinned. `fabric new` records it in `fabric.yaml`
like any module, so the base and the module packs give it the normal skeleton
(projects, `DashboardsModule`, DbContext, tests, tenant column), and the pack
adds its code as per-module files for that module only.

The module takes the project's `module.style`, like every other module, with
no exception for the pack (#608, the owner's decision of 2026-11-12). It was
first pinned to `crud`, which gave a DDD project a module that broke the
project's own rules: plain classes with public setters and a raw `Guid` id,
invariants in a handler, saves outside the unit of work.

- Under `crud` a dashboard is a plain entity, and the save use case checks the
  request before it changes one.
- Under `ddd` a dashboard is an aggregate root, `Dashboard :
  AggregateRoot<DashboardId>`, changed only by `Dashboard.Create` and
  `Rearrange`. They keep its rules (a name of 1 to 100 characters, at most 50
  widgets, unique keys, the 12-column grid) and answer a broken one with a
  `DomainError`, which the Application turns into the same `400` and code the
  CRUD module answers. Whether a widget is one the modules declare for the
  caller stays the use case's, since it asks the widget catalogue. Commands
  carry the module's value objects, and the use cases commit through
  `IDashboardsUnitOfWork`. With `module.ddd-depth: strict`, the identifier and
  the value objects map by convention and the module's strict rules hold for
  it.

- A dashboard row holds its owner (the token's `sub`, read through
  `ICurrentUser`), its name and its widgets as one JSON column. Every read
  filters by owner, and with `backend.tenancy: column` by tenant in the
  persistence layer as for every module.
- The client chooses a dashboard's id (a Guid v7). Saving is `PUT
  /dashboards/{id}`, which creates or replaces, and removing is `DELETE`, so
  every write is idempotent and the base's retry policy (retry only idempotent
  methods) stays correct. Another user's id is not found for the caller, and
  inserting it fails on the primary key: 409, never an overwrite.
- `GET /dashboards` and `GET /dashboards/widgets` are paged.
  `GET /dashboards/widgets/{id}` runs a widget's query as the calling user.
  Saving a dashboard that names a widget that is not declared (or not for the
  caller) is refused with `dashboards.unknown-widget`.
- The module declares two sample widgets of its own (the caller's dashboards as
  a table, their widget counts as a chart), so a fresh project has something to
  put on a dashboard and the shape to copy.

**The frontend** draws the grid with `react-grid-layout` (2.3.0, MIT; its
serialisable `{i, x, y, w, h}` layout is what the module stores) and renders
widgets with the existing ECharts and DataTable components; `grid-layout-plus`
takes its place for Vue later. The page is built for a React single-page app on
RTK Query; with Vue, Orval or micro-frontends the module keeps the router's
placeholder page until a later issue builds it there, and only the grid library
is added as a dependency. The router pack renders the editor in the brought
module's page, a generation-time branch on this choice (ADR-0006), since there
are no per-module slots.

**As built for the Vue page (#214).** The same page for a Vue single-page app
on Orval's TanStack Query client, a branch of the same pack (ADR-0006); nothing
above changes for React.

- **The page** is `DashboardEditor.vue`, `DashboardCanvas.vue` and
  `WidgetView.vue` in `web/app/src/dashboards`, with `Pager.vue` and
  `Failure.vue`, on grid-layout-plus 1.1.1, with the project's `DataChart` and
  `DataTable`. It carries every fix of the React page's review rounds (#201):
  paging, failed reads with "Try again", "Saved." only for the saved
  revision, widgets read again after a save, the 50-widget and row-1000
  limits, the picker's loading guard and pair check, and the pager's clamp
  on a known total only.
- **What differs from React's, and why.** TanStack Query has no `data` that
  keeps the page before, so the picker reads `data` and the list asks for
  `keepPreviousData`. It refetches on focus, so an opened dashboard is copied
  into a draft once, from an answer that is neither cached nor failed, and
  later refreshes leave the draft alone.
- **Shared files.** `layout.ts`, `chartOption.ts`, `uuidV7.ts`,
  `widgetChoice.ts` and their tests use neither framework and are React's
  files; so is `e2e/dashboards.spec.ts`, which branches only on the grid's
  class names.
- **Where it shows.** `vue-router`'s module page renders the editor for the
  brought module, as `react-router`'s does.
- **Where it is not built yet.** With another Vue UI, forms or validation
  library than shadcn-vue, vee-validate and zod, `DashboardCanvas.vue` throws
  when it loads, saying so, as the Vue agent panel does (ADR-0013).
- **Styles.** grid-layout-plus injects its own styles from its script, so the
  page adds nothing to `src/index.css`: its `web.styles` contribution renders
  empty, and the generator drops an empty contribution before it joins a slot
  (#221).

**As built for generative, the backend (#185).** The owner settled the open
choices on #185 by accepting the recommendations put to them
([comment](https://github.com/smartsolutionslab/software-fabric/issues/185#issuecomment-6680464205)),
with React and Vue panels as later steps, and decided on a second escalation
that paged queries become agent tools in the agent-framework pack (#215).

- **The catalog.** `generative` requires `frontend.agent-ui == ag-ui` instead
  of the agents option it implies. It has its own pack,
  `packs/frontend/dashboards/generative`, which adds files to the brought
  `dashboards` module only.
- **The agent lives in the dashboards module**, as ADR-0011 places agents:
  `Agents/DashboardAgent`, run by the query `TalkToTheDashboards`, the agent
  `dashboards` of `POST /agents/{name}` (ADR-0013). Modules do not reference
  each other, so the `assistant` module could not use these queries.
- **Its tools are two queries through the dispatcher:** `ListWidgets`, the
  catalogue the caller may see, a page at a time (a paged query is a tool
  since #215), and `ProposeWidget`. A proposal names a declared widget the
  caller may see, a visual its kind can show and, optionally, the key of the
  draft's widget it replaces; it answers the widget under its declared title
  and saves nothing. An undeclared or hidden widget is
  `dashboards.unknown-widget`, an unfitting visual or key
  `dashboards.invalid-widget`. The browser renders the tool's result as a
  pending widget with Accept and Discard; Accept only changes the draft, and
  the existing Save persists it through every check a save makes.
- **Shared state is the open draft, read-only.** The browser sends the draft's
  widgets (key, source, visual) as the run's AG-UI state. `DashboardDraft.Read`
  checks its shape and size (at most 50 widgets, short texts; otherwise
  `dashboards.invalid-draft`, a 400), and the handler gives the model the
  declared widgets among them, with their declared titles, as one message
  before the conversation, never in the agent's instructions. The host
  endpoint does not change: no state events, and no tools the browser
  declares.
- **The gate** `TheDashboardAgentSavesNothing` fails the build when the
  dashboards module gives an agent a command as a tool, found in the compiled
  code as `EveryAgentToolRequestIsValid` finds tool requests.
- **The stand-in model** (ADR-0013) also calls a tool a message names: a
  message that is an offered tool's name, a space and a JSON object calls that
  tool with those arguments, so the tests can ask for a precise proposal.
  Every other message is answered as before.
- **Tests.** `tests/Modules/Dashboards.Tests` runs the agent and the proposal
  through the dispatcher; `tests/Integration.Tests/GenerativeDashboardsTests`
  runs them through the host with the stand-in model.
- **Then:** the React page, with its end-to-end test, and the Vue page
  (both below).

**As built for generative, the React page (#185).** The page the user-editable
pack builds for a React single-page app on RTK Query gains the agent; with any
other frontend the page stays as `user-editable` leaves it until its own step.

- **The assistant** is `DashboardAssistant` in `web/app/src/dashboards`, a
  file of the generative pack: a conversation with the agent `dashboards`
  through the ag-ui pack's `useAgent`, which takes an optional state getter
  for it, read as each run starts. It sends the open draft's widgets (key,
  source, visual) as the run's state, and shows each tool call as text: a
  proposal by its declared title, a refusal by the host's reason.
- **A proposal waits on the grid.** The assistant reads each `propose_widget`
  result through `readProposal`, a zod schema, since a tool result is
  untrusted, and hands each to `DashboardCanvas` once. The canvas places
  every undecided proposal, one below the other under the draft's widgets, as
  a fixed `PendingWidget` with its live data from
  `GET /dashboards/widgets/{id}` (read again after each save, as a placed
  widget's is), and Accept and Discard. A refused proposal leaves nothing
  pending.
- **Accept only changes the draft**, adding the widget below the others or
  putting it in the place of the one it names (as a new widget when the draft
  no longer has it); the existing Save persists it. Accept is disabled when a
  new widget would not fit, as Add widget is.
- **Where it shows.** The user-editable pack's `DashboardCanvas.tsx` branches
  on `generative` (ADR-0006) to render the assistant and the pending widget,
  rather than a slot; without `generative` it is unchanged.
- **Tests.** `DashboardAssistant.test.tsx` renders the page against a
  server-sent event stream the test writes, with axe where the project has the
  accessibility tests. `e2e/generative-dashboards.spec.ts`, against the
  AppHost with the stand-in model, asks for
  `propose_widget {"source":"dashboards.widget-counts","visual":"bar","replaces":null}`,
  accepts, saves, reloads and finds the chart, and asks for an undeclared
  widget, which is refused with nothing pending or saved. The message names
  `replaces` as `null`: an agent tool's `Option` argument is required, so the
  model states its absence (#215).

**As built for generative, the Vue page (#185).** The same assistant on the Vue
page of #214, a branch of the same packs (ADR-0006); nothing above changes for
React.

- **The files.** `DashboardAssistant.vue` and `PendingWidget.vue` are the
  generative pack's, built from shadcn-vue in a vee-validate form with zod, as
  the Vue agent panel is (ADR-0013). With another Vue UI, forms or validation
  library, `DashboardAssistant.vue` throws when it loads, saying so.
  `proposal.ts` serves both frameworks: besides reading and accepting a
  proposal, it now holds the conversation's framework-free parts
  (`toolResults`, `proposalsOf`, `toolOutcome`), which React's
  `DashboardAssistant.tsx` uses too. Its tests passed unchanged.
- **`useAgent`'s Vue composable** takes the same optional state getter, read
  as each run starts.
- **Where it shows.** `DashboardCanvas.vue` branches on `generative`, as
  React's canvas does. The pending proposals are `static` items of
  grid-layout-plus's layout below the draft's widgets, and the layout is drawn
  again whenever the draft or the pending proposals change. In both
  frameworks the assistant sits below the grid, so the page above the grid,
  and every place on it, is where it is without `generative`.
- **Tests.** `DashboardAssistant.test.ts` is `DashboardAssistant.test.tsx`'s
  cases with Testing Library for Vue, on the generated client and a host the
  test answers. `e2e/generative-dashboards.spec.ts` is React's file; it
  branches only on the grid's class names, as `e2e/dashboards.spec.ts` does.

## Consequences

- Every existing profile takes `frontend.dashboards: []`; its `fabric.yaml`
  gains that one line, as `backend.authorization` and `backend.mcp` did, and
  every other file is unchanged.
- `user-editable` and `generative` are `experimental` (ADR-0003): the first has
  a pack with a React and a Vue page; so does the second, whose backend is
  built too (#185).
- The frontend's checked-in OpenAPI document describes the module's endpoints:
  the API client packs declare the slots it fills.
- A module that wants a widget changes its own Application project only; no
  shared file lists widgets.
- What a widget's query reads is the query's own business: the rule sees that it
  is declared, not that it reads only what its viewers may see.
