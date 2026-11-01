# Errors: Result values

**Status:** Accepted (generated)

## Decision

Expected failures — not found, validation, conflict, forbidden — are values: handlers return `Result<TValue, TError>` where `TError` derives from `ApiError(Code, Message, Status)`, both in `src/Kernel` (their exact shape is the result style's ADR). Exceptions are for the unexpected only.

At the HTTP edge, `ToHttpResult()` turns a success into `200` with the value and a failure into RFC 9457 problem details with the error's status and code.

A failed precondition is the one exception a caller can cause, and only a guard can say that it was one.
{{~ if project["backend.guards"] == "ensure-that" ~}}
Every failed `Ensure.That` guard, a null check included, throws `GuardException` (an `ArgumentException`, `src/Kernel`). The host's exception handler (`GuardExceptionHandler`, `src/BuildingBlocks/Http/ErrorResponses.cs`) answers exactly that type with `400` problem details carrying the code `invalid-argument` and the guard's message, and logs it at Information. Every other exception, a plain `ArgumentException` included, is a bug and answers `500` problem details with no message, type or stack trace.
{{~ else ~}}
With the BCL guards a failed precondition is a `500`. `ArgumentNullException.ThrowIfNull` and its relatives throw plain `ArgumentException`s, which cannot be told apart from one a bug throws (a duplicate dictionary key, a library's own argument check), so the host maps no exception to `400`: every exception answers `500` problem details with no message, type or stack trace. Choose `backend.guards=ensure-that` to answer invalid input with `400`.
{{~ end ~}}

This is a backstop, not a way to report errors: a failure a caller should expect is still a `Result`. Decided by the repository owner on 2026-11-01: only a failed guard becomes `400`; any other `ArgumentException` is a bug.

## Consequences

- A caller cannot ignore a failure without saying so; the compiler shows every place a failure is handled.
- No exception filter decides status codes; the error that knows the failure carries its status.{{ if project["backend.guards"] == "ensure-that" }} The one exception handler knows a single rule: `GuardException` is `400`, everything else `500`.{{ end }}
{{~ if project["backend.guards"] == "ensure-that" ~}}
- A guard that rejects input from an endpoint answers `400`, not `500`; a client cannot tell which field failed beyond the guard's message (field-level validation is a separate choice).
- The handler sees the exception's type, not who threw it: an `Ensure.That` guard that fails because of a bug (a handler's own null check on a value it built) also answers `400`, with its message. Code that is not checking a caller's input throws something else.
- An `ArgumentException` thrown by hand or by a library is a bug and answers `500`; a precondition on a caller's input is written with `Ensure.That`.
{{~ else ~}}
- A value object fed bad input from an endpoint answers `500`, like a bug; a client learns only that the request failed.
{{~ end ~}}
- A domain rule's failure is a `DomainError` without HTTP; the Application decides its status when it maps it with `ApiError.From`. HTTP stays out of the Domain.
