# Errors: Result values

**Status:** Accepted (generated)

## Decision

Expected failures — not found, validation, conflict, forbidden — are values: handlers return `Result<TValue, TError>` where `TError` derives from `ApiError(Code, Message, Status)`, both in `src/Kernel` (their exact shape is the result style's ADR). Exceptions are for the unexpected only.

At the HTTP edge, `ToHttpResult()` turns a success into `200` with the value and a failure into RFC 9457 problem details with the error's status and code.

A failed precondition is the one exception a caller can cause. Guards and value-object factories throw `ArgumentException` (`ArgumentNullException`, `ArgumentOutOfRangeException` and every other subclass included) on bad input, so the host's exception handler (`ArgumentExceptionHandler`, `src/BuildingBlocks/Http`) answers it with `400` problem details carrying the code `invalid-argument` and the guard's message, and logs it at Information. Every other exception answers `500` problem details with no message, type or stack trace. This is a backstop, not a way to report errors: a failure a caller should expect is still a `Result`.

## Consequences

- A caller cannot ignore a failure without saying so; the compiler shows every place a failure is handled.
- No exception filter decides status codes; the error that knows the failure carries its status. The one exception handler knows a single rule: `ArgumentException` is `400`, everything else `500`.
- A guard that rejects input from an endpoint answers `400`, not `500`; a client cannot tell which field failed beyond the guard's message (field-level validation is a separate choice).
- The handler sees the exception's type, not who threw it: an `ArgumentException` from a bug (a duplicate dictionary key, a misused library call) also answers `400`, with its message. Code that is not checking a caller's input throws something else.
- A domain rule's failure is a `DomainError` without HTTP; the Application decides its status when it maps it with `ApiError.From`. HTTP stays out of the Domain.
