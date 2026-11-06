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

  `generative` requires `user-editable` and `backend.llm.agents ==
  agent-framework`. As a multi dimension the two combine without a third option,
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
with `module.style: crud` and `module.cqrs: logical` pinned. `fabric new`
records it in `fabric.yaml` like any module, so the base and the module packs
give it the normal skeleton (projects, `DashboardsModule`, DbContext, tests,
tenant column), and the pack adds its code as per-module files for that module
only. A dashboard is a document saved per user, with no invariants beyond its
shape, so CRUD is its style; pinning it keeps the pack's branches to the
project-wide choices.

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
  page adds a comment to `src/index.css` instead of an import: the
  `web.styles` slot joins its entries with a separator, and an empty entry
  would leave a blank first line that Prettier refuses.

## Consequences

- Every existing profile takes `frontend.dashboards: []`; its `fabric.yaml`
  gains that one line, as `backend.authorization` and `backend.mcp` did, and
  every other file is unchanged.
- `user-editable` and `generative` are `experimental` (ADR-0003): the first has
  a pack, whose React page is still to come; the second has none until #185.
- The frontend's checked-in OpenAPI document describes the module's endpoints:
  the API client packs declare the slots it fills.
- A module that wants a widget changes its own Application project only; no
  shared file lists widgets.
- What a widget's query reads is the query's own business: the rule sees that it
  is declared, not that it reads only what its viewers may see.
