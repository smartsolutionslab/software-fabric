# Agent guide — Software Fabric (the generator repo)

This file is read by every agent that works here, whichever model or
runtime it is: Claude Code (via `CLAUDE.md`, which imports this file),
Codex, or any other tool that reads `AGENTS.md`. Rules that only one
runtime needs go in that runtime's own file, never here.

## What this repo is

The generator, not a generated project. It turns a set of choices from
`catalog/catalog.yaml` into a concrete .NET + React/Vue project. Read
`README.md`, then the ADRs in `docs/adr/` in order.

## Non-negotiables

1. **Generate, don't configure** (ADR-0001). A generated project contains
   only the options that were chosen. No runtime switches between options,
   no `if (options.UseKafka)` in generated code.
2. **A pack touches only its own files and declared slots** (ADR-0002).
   A pack that needs to change another pack's output is a design error —
   add a slot instead, in its own change.
3. **An option without a gate is not done** (ADR-0002). Every pack ships
   code, an ADR, agent rules, and at least one check that fails the build
   of a generated project when the rule is broken.
4. **Same input, same output.** The generator is deterministic. No
   timestamps, random IDs or machine paths in generated files.
5. **Nothing is "supported" that the matrix does not build** (ADR-0003).

## Workflow

Milestones M0 and M1 are **supervised**: stop at every gate in
`docs/roadmap.md` and wait for a human. Agents work autonomously from M2
on, through the board protocol in ADR-0004.

- Branch from `main`; one issue per branch; PRs target `main`.
- Conventional Commits; a scope is one of `.github/commit-scopes`. CI checks every commit a PR adds.
- Tests first for new behaviour; quote the failing output in the PR.

## Board protocol (from M2)

This repo runs the protocol its packs generate (ADR-0004), copied into
`.github/scripts/board/`; a test fails when the copy drifts from the packs.

- **Take work** with `GITHUB_REPOSITORY=smartsolutionslab/software-fabric
  bash .github/scripts/board/next.sh <lane> <executor>`. Lanes: `generator`
  (src/, catalog, schema), `packs-backend`, `packs-frontend`, `ci` (workflows,
  build/). Exit 3: nothing eligible. Exit 1: the claim failed; stop and report
  it. Claude Code: `/next-issue`.
- **Work only on the `agent/<issue>` branch you claimed**, push at
  least every two hours, and end every commit with
  `Agent-Executor: <family>-<runtime>/<detail>`.
- **Open the pull request against `main`** with `## Spec and plan` and
  `## Tests observed failing`; a reviewer of the other model family runs on it.
- **Escalate instead of guessing** with `escalate.sh <issue> needs:decision
  "<question>"` (or `agent:blocked` with the verbatim failure). A decision
  this repo's ADRs do not settle is always a `needs:decision`: agents
  implement decisions, they do not make them.
- An issue with a human assignee belongs to that human.
- **Phases**, each with a brief in `.claude/agents/`: architect (spec and
  plan) → test-writer (tests first, failing output verbatim) → engineer (may
  not edit those tests) → the reviewer for what the diff touches
  (`backend-reviewer` for `src/` and backend packs, `frontend-reviewer`,
  `security-reviewer`). Without subagents, work the same phases yourself, in
  that order.

## Layout (target)

```
catalog/        catalog.yaml — dimensions, options, rules, profiles
schema/         JSON schemas for catalog and pack manifests
src/            the generator (C#)
packs/<dimension>/<option>/
                pack.yaml + templates + docs (adr, agent rules) + gates
tests/          generator tests + matrix runner
docs/adr/       decisions about the generator itself
```
