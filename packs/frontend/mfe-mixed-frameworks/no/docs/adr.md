# Micro-frontends: one framework

**Status:** Accepted (generated)

## Decision

Every remote is written with the shell's framework, **{{ project["frontend.framework"] }}**, at the shell's version,
and shares it with the shell as one instance. A remote that imports another UI framework fails its lint.

## Consequences

- One framework's knowledge, components and tests serve every module; the design system's components work in every
  remote.
- A module cannot be moved to another framework on its own; that would be a change of this decision.
