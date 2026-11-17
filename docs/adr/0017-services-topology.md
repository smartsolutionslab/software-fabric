# ADR-0017: The services topology

**Status:** Proposed (draft for the owner; decisions D1 to D7 open)
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

1. **Routes** `/{module-name}/**` to the module's service with YARP, using
   Aspire service discovery. The route table is built at startup from the
   same convention that names the services, so it has no line per module.
2. **Hosts what exists once per application**: `/me`, the SignalR or SSE
   hub, the MCP server and the AG-UI endpoint.

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
contribute to `edge.*` instead of `host.*` when it is `services`. Today that
is: `base/core` (`/me`), `frontend/realtime/signalr`, `frontend/realtime/sse`,
`frontend/agent-ui/ag-ui`, `backend/mcp/server`, and the endpoints of
`backend/tenancy/*`. Every other pack is unchanged.

### The AppHost discovers services by convention

`apphost.host` stays `exactly-one`: with `services` it adds the edge. The
service hosts are added by a loop over `src/Services/*/Host/*.csproj`
(`AddProject(name, path)`), with the module name as the resource name, so
`add-module` still only creates files. The AppHost project references the
service hosts by glob for the build, as the monolith host references the
modules.

### Data and messaging per service

- **Database per service** on one PostgreSQL (or SQL Server) server: the
  AppHost loop adds `AddDatabase(<module>)` and references it from the
  service. EF Core migrations, the Wolverine outbox and inbox, and Marten's
  event store live in the service's own database.
- **Integration events only.** `module.communication: contract-interface`
  already conflicts with `services`; the bus is required. Each service
  consumes on its own queue.
- **Contracts stay one project** (`src/Contracts`) in the same repository.
  Services reference it, never each other.

### Authentication

The edge forwards the caller's bearer token unchanged; every service host
validates it itself through the auth pack's `host.*` contributions. One
audience for the whole API in V1 (see D4).

### Gates

- `ServicesDoNotReferenceEachOther`: a service host references no other
  module's assemblies (the monolith's `ModulesDoNotReferenceEachOther`
  carries over unchanged).
- `EveryServiceHostRunsExactlyOneModule`: the scan in each host finds one
  `IModule`.
- `SingletonsLiveOnTheEdge`: no service host maps `/me`, a hub, MCP or
  AG-UI.
- The pairwise matrix includes `services`; the nightly run promotes it to
  supported (ADR-0003). A profile `saas-services-vue` (or `-react`) keeps
  the combination generated and tested end to end, with at least two
  services exchanging one integration event.

## Consequences

- A generated services project runs one process per module plus the edge.
  `aspire run` starts them all; the developer experience stays one command.
- Most packs need no change. The work is the topology pack, the edge, the
  AppHost loop, six packs that move their singleton to `edge.*`, and the
  gates.
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

## Open questions for the owner

- **D1 — Edge:** a YARP host project of our own, or Aspire's YARP
  integration if it covers service-discovery routing? (Recommendation: our
  own host project; it also carries the singletons.)
- **D2 — Database per service or schema per service on one database?**
  (Recommendation: database per service; Marten and the outbox already
  separate cleanly.)
- **D3 — Tenancy:** allow `services` with `column` and `schema-per-tenant`
  in V1, or require `backend.tenancy == none` until a pilot needs both?
  (Recommendation: require `none` in V1, as Marten already does.)
- **D4 — Audience:** one audience for the API, or one per service?
  (Recommendation: one in V1.)
- **D5 — gRPC:** `backend.api-protocols: grpc` adds Kestrel endpoints per
  host; route it through the edge too, or require `rest` only in V1?
  (Recommendation: `rest` only in V1.)
- **D6 — Profile:** `saas-services-vue` or `saas-services-react` as the
  tested profile? (PharmaTraining uses Vue.)
- **D7 — Pilot:** PharmaTraining as the services pilot, generated as a
  monolith first and migrated once the pack is supported, or generated
  with `services` as soon as the pack is experimental?
