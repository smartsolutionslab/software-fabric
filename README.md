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

M0 (generator core) is done. M1 (profile `saas-standard-react` end to end)
is done, waiting for its gate review: the base pack, the backend packs, the Aspire integration
tests and the frontend are real (M1-01 to M1-07): a generated app signs a
user in through Keycloak and lists a module's data, checked end to end with
Playwright. Every generated project carries the board protocol (M1-08):
agents claim issues, escalate to developers and get reviewed by another
model family through GitHub alone. CI generates the profile with the CLI
and puts it through its own CI, end-to-end tests included (M1-09). In the
pilot (M1-10) an agent delivered an issue in a generated project through
the board protocol, reviewed by another model family and merged on green CI. `tests/Fabric.Packs.Tests`
generates the profile from the real packs — empty stand-ins for the rest —
builds it, runs its tests, installs, builds, lints and tests its web app, and starts it against real PostgreSQL and RabbitMQ
containers.

| Artifact | Purpose |
|---|---|
| [`catalog/catalog.yaml`](catalog/catalog.yaml) | Every option, its scope and its rules |
| [`docs/catalog-rules.md`](docs/catalog-rules.md) | The rule grammar the catalog uses |
| [`docs/packs.md`](docs/packs.md) | How to write a pack: layout, targets, template model, slots |
| [`schema/pack.schema.json`](schema/pack.schema.json) | What an option pack must declare |
| [`docs/adr/`](docs/adr/) | Decisions 0001–0007 |
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
`build/profile-ci.sh saas-standard-react` generates that profile from the
real packs and runs the generated project's CI steps against it (Docker,
Node 22 with pnpm and the Aspire CLI bundle needed).

## Stack of the generator itself

C# / .NET 10 CLI, distributed as a `dotnet tool`. Templates rendered with
Scriban. Tests in xUnit.
