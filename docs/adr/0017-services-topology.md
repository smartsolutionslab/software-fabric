# ADR-0017: The services topology

**Status:** Accepted (owner, 2026-11-17: D1 to D7 decided, see the end).
Amended (owner, 2026-11-18, #707): the singletons with services, see the
amendment at the end.
**Date:** 2026-11-17

## Context

`backend.topology` offers `modular-monolith` (supported) and `services`
(experimental, `requires: backend.bus.transport != none`), but only the
monolith has a pack. The roadmap defers `services` because "several hosts
need an ADR of their own". This is that ADR.

The first project that needs it is PharmaTraining: seven bounded contexts
(organisation, editorial, catalog, compliance, learning, assessment,
certification) that talk only through integration events over RabbitMQ, two
of them event-sourced with Marten, a Vue frontend and Keycloak.

What the monolith does today, and what a second host breaks:

- **One host.** `backend/topology/modular-monolith` renders `src/Host`, which
  references every module's Api project by glob and discovers each
  `IModule` at startup (ADR-0005).
- **Host slots.** 31 packs contribute to the host through `host.packages`,
  `host.services`, `host.middleware`, `host.endpoints`,
  `host.kestrel-endpoints` and `host.log-levels`. Every slot assumes there is
  exactly one place to put the contribution.
- **AppHost.** `apphost.host` has `cardinality: exactly-one`: the AppHost
  starts one host and wires it with `apphost.host-wiring`.
- **No line per module (ADR-0005).** `add-module` only creates files. A
  services topology must not reintroduce a list of services in a shared
  file, or parallel agents conflict on it.
- **Choices do not change on upgrade (ADR-0016).** Moving a generated
  project from `modular-monolith` to `services` is a migration in that
  project, not a `fabric upgrade`.

## Decision

### One service host per module

With `backend.topology: services`, every module runs in a host of its own:
`src/Services/{{ module.namespace }}/Host`, rendered with `per-module: true`.
The host's project references exactly its own module's Api project, so the
existing assembly scan (`ModuleDiscovery`) finds exactly one `IModule` and
no new discovery code is needed. Grouping several modules into one host is
out of scope (see Alternatives).

### An edge host in front

`src/Edge` is a single host that the frontend talks to. It does two things:

1. **Routes** `/{module-name}/**` to the module's service with YARP in a host
   project of its own (D1), using Aspire service discovery. The route table is built at startup from the
   same convention that names the services, so it has no line per module.
2. **Hosts what exists once per application**: `/me`, the SignalR or SSE
   hub, the MCP server and the AG-UI endpoint. (Amended 2026-11-18: the edge
   hosts no MCP server, and AG-UI is spoken by the owning service and
   forwarded by the edge; see the amendment at the end.)

The frontend, the OpenAPI copy and the end-to-end tests see one origin, as
they do in the monolith. Module paths already start with the module's name,
so the frontend's generated client does not change.

### Host slots keep their names; singletons move to new edge slots

Rather than relabelling 31 packs, the existing `host.*` slots keep their
meaning — "every host that runs module code" — and render into every
service host. That is right for everything that is per process: auth,
data access, the bus, the mediator, errors, the event store, the LLM client.

New slots take what must exist once:

| Slot | Rendered into |
|---|---|
| `edge.packages`, `edge.services`, `edge.middleware`, `edge.endpoints` | `src/Edge` only |

Packs that contribute a singleton branch on the topology (ADR-0006) and
contribute to `edge.*` instead of `host.*` when it is `services`: `base/core`
(`/me`), `frontend/realtime/signalr`, `frontend/realtime/sse`,
`frontend/agent-ui/ag-ui` and `backend/mcp/server`. The tenancy packs need no
branch, since `services` requires `backend.tenancy == none` (D3). (Amended
2026-11-18 for the MCP server and AG-UI; see the amendment at the end.)

### The AppHost discovers services by convention

`apphost.host` stays `exactly-one`: with `services` it adds the edge. The
service hosts are added by a loop over `src/Services/*/Host/*.csproj`
(`AddProject(name, path)`), with the module name as the resource name, so
`add-module` still only creates files. The AppHost project references the
service hosts by glob for the build, as the monolith host references the
modules.

`apphost.host-wiring` keeps its meaning too: its chained calls (references
to the bus, Keycloak, the LLM, `.WaitFor(...)`) apply to every service host
inside the loop. A new slot `apphost.edge-wiring` takes the calls the edge
needs (Keycloak, the hub's backplane, the web app's reference). (Amended
2026-11-18: realtime events reach the edge over the project's bus, with no
backplane; see the amendment at the end.)

### Data and messaging per service

- **Database per service** on one PostgreSQL (or SQL Server) server (D2):
  with `services`, the database packs declare only the server in
  `apphost.resources` and branch (ADR-0006) to leave out the single
  `database`; the loop adds `AddDatabase(<module>)` per service and
  references it under the connection name the service already reads. EF Core migrations, the Wolverine outbox and inbox, and Marten's
  event store live in the service's own database.
- **Integration events only.** `module.communication: contract-interface`
  already conflicts with `services`; the bus is required. Each service
  consumes on its own queue.
- **Contracts stay one project** (`src/Contracts`) in the same repository.
  Services reference it, never each other.

### Authentication

The edge forwards the caller's bearer token unchanged; every service host
validates it itself through the auth pack's `host.*` contributions. One
audience for the whole API (D4).

### Gates

- `ServicesDoNotReferenceEachOther`: a service host references no other
  module's assemblies (the monolith's `ModulesDoNotReferenceEachOther`
  carries over unchanged).
- `EveryServiceHostRunsExactlyOneModule`: the scan in each host finds one
  `IModule`.
- `SingletonsLiveOnTheEdge`: no service host maps `/me`, a hub, MCP or
  AG-UI. (Amended 2026-11-18: a service maps its own MCP server and AG-UI
  endpoint under its module's path; see the amendment at the end.)
- The pairwise matrix includes `services`; the nightly run promotes it to
  supported (ADR-0003). The profile `saas-services-vue` (D6) keeps the
  combination generated and tested end to end, with at least two services
  exchanging one integration event.
- Catalog rules for `services`: `requires: ["backend.bus.transport != none",
  "backend.tenancy == none"]` (D3) and `conflicts: ["backend.api-protocols
  has grpc"]` (D5).

### Options services refuse (#723)

The catalog states what a services project cannot build yet as `conflicts`
on the option, so `fabric new` refuses it with one sentence and the matrix
never picks it:

- **Integration tests run through the AppHost** (owner, 2026-11-18, #723).
  `backend.test.integration == testcontainers` conflicts with `services`, and
  `aspire-fixture`, the dimension's default, is what a services project gets.
  The AppHost already wires every service host, the edge and a database per
  service with its own migration run (D2), so the Aspire fixture tests that
  setup as it runs; a testcontainers fixture would have to rebuild it by hand,
  one host, database and migration run per module, and would test that copy
  instead. A modular monolith keeps both options. A testcontainers fixture for
  services and a silent fallback to `aspire-fixture` were both rejected.
- **The singletons wait for the edge.** `backend.mcp has server`,
  `frontend.realtime == signalr`, `frontend.realtime == sse` and
  `frontend.agent-ui == ag-ui` conflict with `services` for now: each of them
  would map its endpoint in every service host, which `SingletonsLiveOnTheEdge`
  forbids. #707 moves them to `edge.*` and lifts these conflicts in the same
  change. (Amended 2026-11-18: each conflict is lifted in its own sub-issue of
  #707, and the MCP server and AG-UI do not move to the edge as such; see the
  amendment at the end.)

What does work with services: the model and embeddings rules check every
service host's `Program.cs`; agent-framework's endpoint rule starts every
service host; and with `rag`, the vector tables each service's migration run
makes in its own database are what the generated `VectorTablesTests` check.

## Consequences

- A generated services project runs one process per module plus the edge.
  `aspire run` starts them all; the developer experience stays one command.
- Most packs need no change. The work is the topology pack, the edge, the
  AppHost loop, five packs that move their singleton to `edge.*`, the two
  database packs, the catalog rules, the profile and the gates.
- Per-service databases make cross-module queries impossible by
  construction; read models that span modules are built from events.
- Local resource use grows with the module count (one .NET process each).
- A project generated as a monolith moves to services by a migration ADR in
  that project (ADR-0016), not by `fabric upgrade`.

## Alternatives considered

- **Several modules per host** (a `services` setting that groups modules).
  More flexible, but a grouping is a list in a shared file, which ADR-0005
  forbids, and nothing needs it yet. Can be added later as a setting.
- **Relabel every host slot** as `service.*` or `edge.*`. Cleaner names, but
  touches 31 packs and every role slot description for no change in output.
- **No edge; the frontend calls each service.** Needs CORS, one base URL per
  module and per-service OpenAPI clients in the frontend; the frontend packs
  would all branch on the topology.
- **A repository per service.** Breaks the board protocol (ADR-0004), which
  coordinates agents through one repository, and the shared Contracts.

## Owner decisions (2026-11-17)

Decided by the owner in the PharmaTraining planning session, each as
recommended in the proposal unless stated otherwise.

| | Question | Decision |
|---|---|---|
| D1 | Edge | A YARP host project of our own, which also carries the singletons |
| D2 | Data | A database per service on one server |
| D3 | Tenancy | `services` requires `backend.tenancy == none` in V1 |
| D4 | Audience | One audience for the whole API |
| D5 | Protocols | `rest` only: `services` conflicts with `grpc` in V1 |
| D6 | Tested profile | `saas-services-vue` |
| D7 | Pilot | PharmaTraining waits for the pack and is generated with `services` as soon as the option builds (experimental), not as a monolith first. That makes "the pack generates a running seven-service project" the pilot's entry gate. |

## Amendment (owner, 2026-11-18): the singletons with services (#707)

The Decision above puts `/me`, the SignalR hub or SSE stream, the MCP server
and the AG-UI endpoint on the edge. `/me` moved there with #705. The other
three each reach module code in process in a monolith host: a module's
consumers send browser events through `IBrowserEvents`, the AG-UI endpoint
sends the agent's query through `IDispatcher`, and the MCP server dispatches
the `[McpTool]` requests. The edge runs no module code, and the Decision
did not say how the hub's events reach it, beyond naming a backplane.

The owner decided on #707 (comments of 2026-11-18, and its **Decisions**
section). The text above stands; where this section differs, it takes
precedence.

- **MCP server: the edge hosts none.** With services, each service is its
  own MCP server, at `/{module}/mcp`. `backend/mcp/server` contributes
  nothing to `edge.*`. The MCP server tests are generated once per service,
  each project starting its own service host and testing that service's
  `/{module}/mcp`; with the modular monolith, `tests/Mcp.Server.Tests` stays
  as it is, one project against `src/Host`.
- **AG-UI: spoken by the owning service, forwarded by the edge.** With
  services, the service that owns an agent speaks AG-UI, at
  `POST /{module}/agents/{name}`. The edge learns a route table, agent name
  to owning service, from the services at startup, and forwards
  `POST /agents/{name}` with it. The edge reads that table with a
  **client-credentials client of its own**, not through an anonymous
  internal endpoint.
- **Realtime: over the bus.** With services, a service's `IBrowserEvents`
  publishes a browser-event message on the project's bus, and the edge
  consumes it and pushes it to the browsers connected to its hub or stream.
  The message is `BrowserEventV1`, in the BuildingBlocks: non-durable, and
  not an integration event. No new AppHost resource is added; there is no
  backplane. The edge is on the broker only when a realtime option is
  chosen.
- **One edge replica in V1**, decided with the realtime message: V1
  supports one edge replica.
- **The endpoints a service maps under its own module's path**,
  `/{module}/mcp` and `/{module}/agents/{name}`, are that service's, not
  singletons the edge hosts.
- **The conflicts are lifted one by one.** The "Options services refuse
  (#723)" section says #707 lifts all four in one change. Instead, each
  conflict is lifted in the sub-issue of #707 that makes its option work:

  | Issue | Does |
  |---|---|
  | #764 | These amendments, to this ADR, ADR-0013 and ADR-0010 |
  | #765 | A service host knows its module's route name |
  | #766 | The MCP server per service; lifts `backend.mcp has server` |
  | #767 | The edge on the bus |
  | #768 | SignalR and SSE on the edge; lift `frontend.realtime == signalr` and `== sse` |
  | #769 | The AG-UI forward; lifts `frontend.agent-ui == ag-ui` |

  The MCP client's `DeclaredServers` under services is #759. #707 closes when
  its sub-issues have merged.

ADR-0013 (AG-UI) and ADR-0010 (the MCP server) state the same for services,
in amendments of their own.
