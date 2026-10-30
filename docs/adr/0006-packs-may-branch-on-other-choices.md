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
options — would multiply slots by the size of the catalog.

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

## Consequences

- Wolverine, Keycloak and the Shouldly ban list use branches today; each
  is visible in the template, next to the code it changes.
- Reviewing a pack means reading its branches against the catalog's
  options for the dimension it reads.
