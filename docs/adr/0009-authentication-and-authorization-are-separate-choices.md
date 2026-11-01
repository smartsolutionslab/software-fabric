# ADR-0009: Authentication and authorization are separate choices

**Status:** Accepted
**Date:** 2026-11-01

## Context

`backend.auth` chooses an identity provider (Keycloak, Entra ID, a generic
OIDC provider), and every generated project authenticates: each module route
requires a signed-in user (the Keycloak pack's `group.RequireAuthorization()`
on `modules.endpoints`), and the browser signs in through
`frontend.auth-client`. Two kinds of project fall outside that:

- one that has no users at all (an internal tool behind a gateway, a demo, a
  service only other services call), which today cannot be generated without
  an identity provider and a sign-in screen;
- one where being signed in is not enough and access depends on who the user
  is: their roles, or information in their profile (a department, a verified
  e-mail address, a custom attribute). The catalog has no way to say so, so
  the generated project has no rule for it and no gate.

The repository owner decided both on 2026-11-01 (issue #71).

## Decision

**A project chooses whether it authenticates at all, and, if it does, how
access is decided.**

- `backend.auth` gets the option `none`: no authentication. Every endpoint is
  anonymous and there is no sign-in.
- A new dimension `backend.authorization`, project scope, `multi: true`,
  `active-when: backend.auth != none`, default `[]`:

  | Value | Access |
  |---|---|
  | `[]` | Every module endpoint requires a signed-in user and nothing more: today's behaviour, now explicit. |
  | `[roles]` | By role: the identity provider's roles in the token. |
  | `[claims]` | By claim or profile information, e.g. `department`, `email_verified`, a custom attribute. |
  | `[roles, claims]` | Both, and a policy may require a role and a claim together. |

- `frontend.auth-client` gets `active-when: backend.auth != none`: without
  authentication there is nothing to sign in to.

**Authorization is its own dimension, not part of `backend.auth`.** The
identity provider decides who signs in and what the token carries; the
authorization choice decides what a module does with it. The two vary
independently: every provider can be combined with every set of authorization
options, and folding them into one dimension would need an option per pair
(`keycloak-roles`, `entra-id-claims`, ...), the multiplication ADR-0006 and
ADR-0008 avoid elsewhere. As a multi dimension, roles and claims combine
without a third option for "both".

**It is project scope for now.** One choice per project keeps the first packs
to one way of declaring policies. Module scope can come later, with the same
options, when a project needs modules that decide access differently.

## Consequences

- No profile changes its choices: every profile authenticates and takes
  `backend.authorization: []`, which is what it generated before.
- `backend.auth=none`, `backend.authorization=roles` and
  `backend.authorization=claims` have no packs yet, so they are
  `experimental` (ADR-0003). Until they have packs, `fabric new` fails with
  `no pack for ...` for them, as for every unbuilt option. The packs are their
  own issues. Each ships its agent rules and a gate (ADR-0002), and an option
  becomes `supported` once the matrix builds it and it passes.
- Packs that branch on `backend.auth` (ADR-0006) gain a value to handle: with
  `none` they must render something valid or fail loudly, never silently
  assume a provider.
