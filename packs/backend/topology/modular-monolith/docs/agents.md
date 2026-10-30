## Topology: modular monolith

- All modules run in `src/Host`. Do not add a second host or a per-module `Program.cs`.
- A module never references another module's projects. Cross-module communication goes through `src/Contracts`.
- Host-wide middleware and services belong to the pack that needs them, through `host.services` / `host.middleware` in `src/Host/Program.cs` — not inside a module.
