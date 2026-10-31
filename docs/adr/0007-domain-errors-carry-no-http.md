# ADR-0007: Domain errors carry no HTTP

**Status:** Accepted
**Date:** 2026-10-31

## Context

The M1-10 pilot's first aggregate had nowhere to put its errors. The
generated AGENTS.md (`backend.errors.result`) placed a module's errors, as
`ApiError` records with an HTTP status, in the Application project; the DDD
packs have aggregates enforce invariants in the Domain project, which may not
reference Application. The pilot agent put HTTP errors in the Domain and the
Codex review flagged it. Issue #9 laid out three options; option 1 was chosen.

## Decision

- **A domain rule fails with a `DomainError`**, an abstract record of `Code`
  and `Message` in `src/Kernel`, with no HTTP status. A module's domain errors
  are sealed records deriving from it, beside their aggregate.
- **The Application maps it** to an `ApiError` with
  `ApiError.From(error, status)`, choosing the status there, typically in the
  module's `<Name>Failures` class. `Result.MapError(...)` translates a whole
  result in one call.
- **`Unit`** in `src/Kernel` is the value of an operation that succeeds with
  nothing to return, so such an operation is `Result<Unit, DomainError>`.
- **The gate**: an architecture rule of `module.style.ddd` fails when a type in
  a Domain project is an `ApiError` or returns one.

`DomainError`, `Unit`, `ApiError.From` and `MapError` come from the result-style
packs (`backend.result-style.*`), which own Kernel's error vocabulary. The DDD
pack's agent rules mention them only when `backend.errors` is `result`
(ADR-0006); its rule checks by type name, so it compiles without them.

## Consequences

- HTTP stays out of the Domain; an aggregate's tests need no status codes.
- One extra step per failure that reaches a caller: the mapping in Application.
  It is also where a status is decided, which is an application concern.
- Projects whose errors are exceptions are unaffected.
