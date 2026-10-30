# Software Fabric

A generator for larger industrial and SaaS applications — .NET backend,
React or Vue frontend — built so that 10–20 coding agents can work on the
generated project in parallel, coordinated through GitHub.

Two halves, one repo:

- **The generator.** You pick options from [`catalog/catalog.yaml`](catalog/catalog.yaml)
  (database, event sourcing, message bus, CQRS, DDD depth, test strategy,
  micro-frontends, state management, UI library, …) or start from a profile.
  The generator produces a concrete project with those decisions fixed —
  code, ADRs, agent rules and the gates that enforce them.
  See [ADR-0001](docs/adr/0001-generate-dont-configure.md).
- **The fabric.** Every generated project ships with the agent setup:
  `AGENTS.md`, Claude Code agents and skills, a GitHub board protocol in
  which agents claim their own issues and escalate to developers, and
  cross-model review. See [ADR-0004](docs/adr/0004-github-is-the-coordination-bus.md).

## Status

Milestone **M0 — core**, not yet started in code. What exists:

| Artifact | Purpose |
|---|---|
| [`catalog/catalog.yaml`](catalog/catalog.yaml) | Every option, its scope and its rules |
| [`docs/catalog-rules.md`](docs/catalog-rules.md) | The rule grammar the catalog uses |
| [`schema/pack.schema.json`](schema/pack.schema.json) | What an option pack must declare |
| [`docs/adr/`](docs/adr/) | Decisions 0001–0004 |
| [`docs/roadmap.md`](docs/roadmap.md) | M0–M4 and the M0 task list |

## Stack of the generator itself

C# / .NET 10 CLI, distributed as a `dotnet tool`. Templates rendered with
Scriban. Tests in xUnit.
