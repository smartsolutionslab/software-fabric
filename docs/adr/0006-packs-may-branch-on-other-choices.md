# ADR-0006: Packs may branch on other choices at generation time

**Status:** Accepted
**Date:** 2026-10-30

## Context

ADR-0002 makes packs independent: a pack owns its files and reaches into
others only through slots. That covers most cases. It does not cover a
pack whose own output depends on another dimension's value — for
example, Wolverine's messaging code differs by database (PostgreSQL or
SQL Server envelope storage) and by transport (RabbitMQ, Azure Service
Bus, Kafka), and the Keycloak realm needs tenant groups only when a
tenancy option is chosen.

Slots cannot express this: a contribution to a slot the other pack does
not declare is an error, and the alternative — one slot per pair of
options — would multiply slots by the size of the catalog. (A
contribution marked `optional: true` is the one exception, ADR-0002: it
is dropped when no selected pack declares its slot. It covers a pack that
adds to another choice's slot when that choice is present; it does not
let a pack's own output differ by choice.)

## Decision

**A pack may read another dimension in its templates** —
`{{ if project["backend.database"] == "postgres" }}` — when its own
output genuinely differs by that choice.

The rules:

1. **Generation time only.** The branch decides what the generated
   project contains; the generated code never branches on a choice
   (ADR-0001 is unchanged).
2. **Every branch is a supported combination or says it is not.** A
   branch for an option the pack does not handle yet still renders
   something valid or fails loudly; it never silently renders nothing.
3. **Prefer a slot** when the other pack can declare one for all its
   options. A branch is for output that belongs to *this* pack.
4. **The matrix covers the branches** (ADR-0003): the pairwise run picks
   up each branch through the combinations it already generates.

**A whole file may branch too.** A `pack.yaml` file entry may carry
`when: <Scriban condition>`, evaluated against the same model as the
templates (`when: project["backend.llm"] != "none"`). This lets the base
ship a file only some choices need without changing any other choice's
output.

- `false` leaves the file out; `true` renders it as any other file.
- A condition that does not parse, fails, or yields anything but `true` or
  `false` is a generation error naming the pack and the file — rule 2:
  never a silent `false`. Reading a key the model lacks fails too, unlike
  in a template, so a misspelled dimension cannot drop a file unnoticed.
- A template that renders empty is still written. Only `when` decides
  whether a file exists; an empty render is not a condition.
- The target is claimed by the pack whatever the condition says
  (ADR-0002), so two packs cannot split a path between them by condition.

## Consequences

- Wolverine, Keycloak and the Shouldly ban list use branches today; each
  is visible in the template, next to the code it changes.
- Reviewing a pack means reading its branches against the catalog's
  options for the dimension it reads — in its templates and in the
  `when` of its file entries.
