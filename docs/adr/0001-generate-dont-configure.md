# ADR-0001: Generate, don't configure

**Status:** Proposed
**Date:** 2026-10-30

## Context

The goal is a starting point for larger .NET + React/Vue applications in
which many implementation choices are open: database, event sourcing,
message bus, CQRS, DDD depth, test strategy, micro-frontends, state
management, UI library and more (`catalog/catalog.yaml`).

There are two ways to make those choices open.

1. **Runtime configuration.** One codebase contains every option; a
   setting picks which one runs.
2. **Generation.** A generator produces a project that contains only the
   chosen options, with nothing left to switch.

The generated project is meant to be built by 10–20 coding agents working
in parallel. Agents are most reliable when a decision is already made and
mechanically enforced; every open alternative in the code is a place where
an agent has to infer which one applies, and in a codebase built by many
agents those inferences diverge.

## Decision

**Choices are made once, at generation time.** The generated project
contains only the chosen options. It records them in `fabric.yaml` at its
root, and each choice arrives with:

- its code,
- an ADR in the project explaining the choice,
- a section in the project's `AGENTS.md` and additions to agent briefs,
- at least one gate that fails the project's build when the rule is broken.

A generated project never contains `if (options.UseKafka)`-style
branching between catalog options.

Choices that are **per module** (`scope: module` — modelling style, DDD
depth, CQRS, communication) are made when the module is created, with
`fabric add-module`, which uses the same packs.

Changing a **project-scoped** choice later is a migration with an ADR in
that project, not a setting. Changing a **module-scoped** choice is an
ordinary issue the generator supports.

## Consequences

- An agent in a generated project reads one set of rules, not a set of
  rules and a matrix of exceptions.
- The generator carries the combinatorial cost instead of the project:
  that is what ADR-0002 (packs) and ADR-0003 (tiers and matrix) are for.
- A project cannot be "re-generated" over local changes. Upgrades to a
  pack reach existing projects as a diff or an issue, not a re-run —
  designing that is out of scope for V1.
- The generator itself must be deterministic; otherwise two projects with
  the same `fabric.yaml` would differ, and the record would lie.
