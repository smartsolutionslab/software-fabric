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

Milestone **M0 — core**: all twelve tasks done, waiting at the M0 gate
(review of the slot set and the rule evaluator). The generator works end
to end against the stub packs in `tests/fixtures/minimal`; the real
catalog has no packs yet — those start in M1.

| Artifact | Purpose |
|---|---|
| [`catalog/catalog.yaml`](catalog/catalog.yaml) | Every option, its scope and its rules |
| [`docs/catalog-rules.md`](docs/catalog-rules.md) | The rule grammar the catalog uses |
| [`docs/packs.md`](docs/packs.md) | How to write a pack: layout, targets, template model, slots |
| [`schema/pack.schema.json`](schema/pack.schema.json) | What an option pack must declare |
| [`docs/adr/`](docs/adr/) | Decisions 0001–0004 |
| [`docs/roadmap.md`](docs/roadmap.md) | M0–M4 and the M0 task list |

## Using it

```bash
dotnet build Fabric.slnx -c Release
```

```bash
fabric() { dotnet src/Fabric.Cli/bin/Release/net10.0/fabric.dll "$@"; }
fabric profiles
fabric validate --profile saas-standard-vue --set backend.database=sqlserver
fabric new ../shop --profile everything --catalog tests/fixtures/minimal/catalog.yaml --packs tests/fixtures/minimal/packs
fabric add-module billing --project ../shop --catalog tests/fixtures/minimal/catalog.yaml --packs tests/fixtures/minimal/packs
```

`build/profiles.sh` is what CI runs after the tests: every fixture profile
generated twice and compared, a module added, every real profile validated.

## Stack of the generator itself

C# / .NET 10 CLI, distributed as a `dotnet tool`. Templates rendered with
Scriban. Tests in xUnit.
