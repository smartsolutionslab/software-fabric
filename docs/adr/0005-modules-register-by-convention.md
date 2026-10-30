# ADR-0005: Modules register by convention

**Status:** Proposed
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

## Consequences

- `add-module` stays a pure file creator, and parallel module work does
  not conflict on registration files.
- The generator needs no per-module slots (docs/slots.md).
- Startup cost of an assembly scan over a handful of module assemblies —
  negligible, and it happens once.
- Discovery hides wiring: a module that fails to register is found by a
  test, not by reading `Program.cs`. The base ships an architecture test
  that every module assembly has exactly one `IModule`.
- IDE users run one `dotnet sln add` after `add-module`.
