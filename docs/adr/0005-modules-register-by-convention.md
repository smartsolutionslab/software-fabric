# ADR-0005: Modules register by convention

**Status:** Accepted
**Date:** 2026-10-30

## Context

`fabric add-module` only creates files: it never rewrites a file that
may have changed since generation (ADR-0001). A generated project still
has to build, run and test a module the moment its files exist.

Most project templates register modules in a list — a solution file, a
`ProjectReference` per module in the host, a `services.AddBillingModule()`
line per module in `Program.cs`, a route per module in the frontend. Each
list is a shared file that `add-module` would have to edit, and that 20
agents adding modules in parallel would conflict on.

## Decision

**Nothing in a generated project needs a line per module.**

- Builds find module projects with globs: `build.proj`
  (Microsoft.Build.Traversal) and the host's `ProjectReference` both use
  `src/Modules/*/…` patterns.
- Each module exposes one `IModule` implementation; the host discovers
  modules by assembly scan and lets each register its services and map
  its endpoints.
- Package references per layer come from `src/Modules/Directory.Build.props`,
  keyed on the project-name suffix.
- A pack that needs code in every module puts a generic implementation in
  BuildingBlocks; the per-module file derives from it.
- The frontend finds module routes with `import.meta.glob`.
- The `.slnx` lists the projects present at generation; `add-module`
  prints the `dotnet sln add` command rather than editing it. CI builds
  `build.proj`, so a stale solution file cannot break CI.

**A pack may bring a module.** Some options are a bounded context of their
own, a dashboards module for example. Such a pack declares the module in
`pack.yaml` with the module-scoped choices it pins
(`modules: { dashboards: { module.style: crud } }`, schema/pack.schema.json).

- `fabric new` adds each module the selected packs bring, in name order, as
  `add-module` would: pins first, then the project's module defaults. It is
  recorded in `fabric.yaml` as any module, and the module packs give it the
  normal skeleton. The declaring pack adds the module's own code as
  per-module files with `when: module.name == "<name>"` (ADR-0006).
- A pin the catalog does not allow is a generation error naming the pack;
  two packs that bring the same module and pin a dimension differently fail,
  naming both.
- `add-module` refuses a name a selected pack brings: that module's choices
  belong to the pack, and add-module would otherwise take the module defaults.
- Only a base or project-scoped pack may bring a module. Which module-scoped
  packs are selected follows from the modules, so a module-scoped pack that
  brought one would decide its own selection.

## Consequences

- `add-module` stays a pure file creator, and parallel module work does
  not conflict on registration files.
- A project without a pack that brings a module generates exactly as
  before: no module, `modules: {}`.
- The generator needs no per-module slots (docs/slots.md).
- Startup cost of an assembly scan over a handful of module assemblies —
  negligible, and it happens once.
- Discovery hides wiring: a module that fails to register is found by a
  test, not by reading `Program.cs`. The base ships an architecture test
  that every module assembly has exactly one `IModule`.
- IDE users run one `dotnet sln add` after `add-module`.
