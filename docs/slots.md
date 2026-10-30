# The slot set (M1)

What a generated project looks like, which pack owns which file, and the
slots through which packs reach into files they do not own. Written for
the 37 packs of `saas-standard-react` plus the base; later profiles add
slots in their own change (ADR-0002).

**Status: accepted at the M0 gate (2026-10-30).** Generator changes 1–3
below are built (M1-01).

## Layout of a generated project

```
fabric.yaml                     generator
README.md  CLAUDE.md            base
AGENTS.md                       base; every pack's agents-md via agents.sections
.claude/agents/<role>.md        base; every pack's brief for that role via agents.brief.<role>
docs/adr/NNNN-<dimension>-<option>.md
                                generator; one per pack, numbered in selection order
global.json  .editorconfig  Directory.Build.props  Directory.Packages.props  build.proj
                                base
src/
  AppHost/                      base (Aspire composition root)
  ServiceDefaults/              base
  BuildingBlocks/               base; packs add their own files under it
  Contracts/                    base
  Host/                         backend.topology.modular-monolith
  Modules/
    Directory.Build.props       base; per-layer package references by slot
    <Module>/<Module>.Domain|Application|Infrastructure|Api/
                                base owns the csproj files (per module);
                                module.style / module.cqrs / ... own the code
tests/
  Directory.Build.props         base; test packages and usings by slot
  Architecture.Tests/           base; packs add gate tests as their own files
  Integration.Tests/            backend.test.integration.aspire-fixture
  Modules/<Module>.Tests/       base (per module)
web/
  package.json  pnpm-workspace.yaml   frontend.workspace.pnpm-workspaces
  app/                          frontend.framework.react
.github/workflows/ci.yml        base; steps by slot
```

## Modules register by convention, never by list

`fabric add-module` only creates files (ADR-0001), so nothing may need a
line per module in a shared file. Proposed in ADR-0005:

- **Build:** `build.proj` (Microsoft.Build.Traversal) and the Host pick up
  module projects with globs — `src/Modules/*/*.Api/*.Api.csproj` — not a
  list.
- **Runtime:** each module has one `<Module>Module.cs` implementing
  `IModule` from BuildingBlocks; the Host discovers modules by assembly
  scan and calls their registration.
- **Packages per layer:** `src/Modules/Directory.Build.props` adds
  package references by project-name suffix (`.Domain`, `.Infrastructure`,
  …), so a pack reaches every module's layer through one project-level
  slot.
- **Per-module code stays generic:** a pack that needs something in every
  module (a DbContext, a tenant filter, handler discovery) puts a generic
  implementation in BuildingBlocks and lets the per-module file inherit
  from it. **No per-module slots** — the generator does not need them.
- **Frontend modules** are found with `import.meta.glob('./modules/*/routes.tsx')`.
- **Solution file:** `<Name>.slnx` lists the projects at generation time;
  `add-module` prints the `dotnet sln add` line instead of editing it.
  `build.proj` is what CI builds, so a stale `.slnx` never breaks a build.

## Slots

Cardinality `0+` unless stated. "Separator" is what joins contributions
(new, see Generator changes); default is a newline.

### Agent setup and docs

| Slot | File | Separator | Filled by (saas-standard-react) |
|---|---|---|---|
| `agents.sections` | `AGENTS.md` | blank line | **generator**: every selected pack's `agents-md`, in selection order |
| `agents.brief.<role>` | `.claude/agents/<role>.md` | blank line | **generator**: every pack's brief for that role |
| `readme.sections` | `README.md` | blank line | packs that change how to run the project (aspire, pnpm, keycloak) |

### .NET build

| Slot | File | Filled by |
|---|---|---|
| `dotnet.package-versions` | `Directory.Packages.props` | every pack that brings a NuGet package |
| `dotnet.build-properties` | `Directory.Build.props` | packs adding analyzers or properties (result, red-first) |
| `modules.packages.domain` | `src/Modules/Directory.Build.props` | — (Domain stays dependency-free) |
| `modules.packages.application` | 〃 | wolverine, mediator |
| `modules.packages.infrastructure` | 〃 | ef-core, postgres, wolverine |
| `modules.packages.api` | 〃 | minimal-apis |
| `buildingblocks.packages` | `src/BuildingBlocks/BuildingBlocks.csproj` | ef-core, wolverine, result |
| `tests.packages` | `tests/Directory.Build.props` | xunit, shouldly, nsubstitute, aspire-fixture |
| `tests.usings` | `tests/Directory.Build.props` | xunit, shouldly, nsubstitute |

**One package, one owner.** A `PackageVersion` is declared by exactly
one pack; others only reference it. Two packs declaring the same version
is a defect in the second pack.

### Composition root and host

| Slot | File | Cardinality | Filled by |
|---|---|---|---|
| `apphost.packages` | `src/AppHost/AppHost.csproj` | 0+ | postgres, rabbitmq, keycloak, react |
| `apphost.resources` | `src/AppHost/Program.cs` | 0+ | postgres, rabbitmq, keycloak, react (Vite app) |
| `apphost.host` | `src/AppHost/Program.cs` | **exactly-one** | backend.topology (adds the host project) |
| `apphost.host-wiring` | `src/AppHost/Program.cs` | 0+ | postgres, rabbitmq, keycloak (`.WithReference(...).WaitFor(...)`) |
| `host.packages` | `src/Host/Host.csproj` | 0+ | keycloak, wolverine, ef-core |
| `host.services` | `src/Host/Program.cs` | 0+ | keycloak, wolverine, result (problem details), tenancy |
| `host.middleware` | `src/Host/Program.cs` | 0+ | keycloak, tenancy |
| `modules.infrastructure` | `src/BuildingBlocks/Modules/ModuleInfrastructure.cs` | 0+ | ef-core, wolverine, tenancy |
| `persistence.model-conventions` | `src/BuildingBlocks/Persistence/ModuleDbContext.cs` (owned and declared by ef-core) | 0+ | tenancy column |

### Frontend

| Slot | File | Separator | Filled by |
|---|---|---|---|
| `web.dependencies` | `web/app/package.json` | `,` + newline | react, redux, rtk, router, rhf, zod, tailwind, shadcn, tanstack-table, echarts, oidc |
| `web.dev-dependencies` | 〃 | `,` + newline | vitest, testing-library, playwright, axe, rtk codegen |
| `web.scripts` | 〃 | `,` + newline | rtk-query-codegen (`codegen`), accessibility |
| `web.vite-plugins` | `web/app/vite.config.ts` | `,` + newline | tailwind |
| `web.imports` | `web/app/src/main.tsx` | newline | redux, router, oidc |
| `web.providers` | `web/app/src/main.tsx` | `,` + newline | redux, oidc, router — wrappers composed in order |
| `web.styles` | `web/app/src/index.css` | newline | tailwind, shadcn |

### CI

| Slot | File | Filled by |
|---|---|---|
| `ci.backend-steps` | `.github/workflows/ci.yml` | aspire-fixture (Docker), red-first (none) |
| `ci.frontend-steps` | 〃 | pnpm, rtk-query-codegen (generated client is current), accessibility |

## Generator changes this needs

1. **Slot separators.** `declares-slots` gains `separator` (default
   newline). JSON lists and object entries need `,` between contributions
   and none after the last; templates cannot know which contribution is
   last.
2. **The generator writes what every pack already declares.** Today
   `adr`, `agents.agents-md` and `agents.briefs` are validated but not
   used. The generator renders each pack's ADR to
   `docs/adr/NNNN-<dimension>-<option>.md` (numbered in selection order)
   and contributes `agents-md` and briefs to the reserved slots
   `agents.sections` and `agents.brief.<role>`, which the base declares.
   Packs may not contribute to those slots themselves.
3. **A project name.** Templates need `name` (lower kebab-case, from the
   target directory unless `--name` is given) and `namespace`
   (PascalCase). Recorded in `fabric.yaml`.

## Known open point

`frontend.framework.react` owns `web/app/`. That fits a single SPA; for
`multi-app` and `micro-frontends` (M2) the app layout belongs to the
architecture pack and the framework pack must contribute into it. That
recut is M2's first task, not a reason to design for it now.
